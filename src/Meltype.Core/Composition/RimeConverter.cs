// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ballban

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meltype.Composition;

/// <summary>
/// Pinyin to Chinese with librime (native/rime). One Rime session per instance, used under a lock: Candidates comes
/// from the UI thread on every key, Learn from the thread pool. The dictionaries are deployed on a background thread;
/// until then IsReady is false and there are no candidates. Failures are logged and leave IsReady false.
/// </summary>
public sealed unsafe partial class RimeConverter : IChineseConverter, IDisposable
{
    /// <summary>The schema in native/rime/data.</summary>
    public const string SchemaId = "meltype_pinyin";

    private const int CacheSize = 64;
    // Learn looks for the committed text among this many candidates (Candidates rarely shows more).
    private const int LearnScan = 100;

    // librime is one engine per process: RimeSetup may run only once, and the folders are fixed until RimeFinalize.
    // Instances with the same folders share the engine; the last one to be disposed finalizes it.
    private static readonly Lock s_engineGate = new();
    private static Task<bool>? s_engine;
    private static string? s_engineFolders;
    private static int s_engineUsers;
    private static bool s_setUp, s_initialized;
    private static nint s_library;
    private static delegate* unmanaged<nuint, nuint, int> s_highlight;

    private readonly Lock _gate = new();
    private readonly bool _usesEngine;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache = [];
    private readonly LinkedList<CacheEntry> _recent = [];
    private nuint _session;
    private bool _ready, _disposed;

    /// <param name="libraryDirectory">The folder with librime (rime.dll / librime.so.1 / librime.1.dylib). PATH is not searched.</param>
    /// <param name="sharedDataDirectory">Rime's shared data: default.yaml, meltype_pinyin.schema.yaml and the dictionaries.</param>
    /// <param name="userDataDirectory">Where Rime keeps the compiled dictionaries and the user dictionary (what it learned).</param>
    public RimeConverter(string libraryDirectory, string sharedDataDirectory, string userDataDirectory)
    {
        var library = Path.GetFullPath(libraryDirectory);
        var shared = Path.GetFullPath(sharedDataDirectory);
        var user = Path.GetFullPath(userDataDirectory);
        var folders = string.Join('\n', library, shared, user);
        Task<bool> engine;
        lock (s_engineGate)
        {
            if (s_engine is null)
            {
                s_engine = Task.Run(() => StartEngine(library, shared, user));
                s_engineFolders = folders;
            }
            else if (s_engineFolders != folders)
            {
                Diagnostics.Log.Warn("Rime は別のフォルダーで動いているため、使えません (1 つのプロセスで 1 つだけ)。");
                WhenReady = Task.FromResult(false);
                return;
            }
            s_engineUsers++;
            engine = s_engine;
        }
        _usesEngine = true;
        WhenReady = engine.ContinueWith(started => started.Result && OpenSession(), TaskScheduler.Default);
    }

    /// <summary>
    /// librime from the MELTYPE_RIME folder, else from rime\ next to the app; each holds the library and data\.
    /// Null when neither has them.
    /// </summary>
    public static RimeConverter? TryCreate(string userDataDirectory)
    {
        foreach (var root in new[] { Environment.GetEnvironmentVariable("MELTYPE_RIME"), Path.Combine(AppContext.BaseDirectory, "rime") })
        {
            if (string.IsNullOrEmpty(root) || FindLibrary(root) is null) continue;
            var data = Path.Combine(root, "data");
            if (Directory.Exists(data)) return new RimeConverter(root, data, userDataDirectory);
        }
        return null;
    }

    public bool IsReady => Volatile.Read(ref _ready);

    /// <summary>Completes once the dictionaries are deployed and the session is open (true), or when Rime cannot be used (false).</summary>
    public Task<bool> WhenReady { get; }

    public IReadOnlyList<string> Candidates(string pinyin, int max)
    {
        if (max <= 0 || pinyin.Length == 0 || !IsReady) return [];
        lock (_gate)
        {
            if (_session == 0) return [];
            if (_cache.TryGetValue(pinyin, out var node) && (node.Value.Texts.Length >= max || node.Value.Complete))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                return node.Value.Texts.Length > max ? node.Value.Texts[..max] : node.Value.Texts;
            }
            try
            {
                var texts = Covering(pinyin, max).ToArray();
                Remember(new CacheEntry(pinyin, texts, Complete: texts.Length < max));
                return texts;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"Rime で変換できませんでした: {ex.Message}");
                return [];
            }
            finally
            {
                RimeClearComposition(_session);
            }
        }
    }

    public void Learn(string pinyin, string text)
    {
        if (pinyin.Length == 0 || text.Length == 0 || !IsReady) return;
        lock (_gate)
        {
            if (_session == 0) return;
            try
            {
                var index = Covering(pinyin, LearnScan).IndexOf(text);
                if (index < 0) return;
                // Selecting a candidate that covers the whole input commits it, and Rime learns from the commit.
                if (RimeSelectCandidate(_session, (nuint)index) != 0) TakeCommit();
                _cache.Clear();
                _recent.Clear();
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"Rime に学習させられませんでした: {ex.Message}");
            }
            finally
            {
                RimeClearComposition(_session);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Volatile.Write(ref _ready, false);
            if (_session != 0) _ = RimeDestroySession(_session);
            _session = 0;
            _cache.Clear();
            _recent.Clear();
        }
        if (!_usesEngine) return;
        lock (s_engineGate)
        {
            if (--s_engineUsers > 0 || s_engine is not { IsCompleted: true }) return;
            // An engine still deploying is left running (stopping it then could crash); a later instance can reuse it.
            if (s_initialized) RimeFinalize();
            s_initialized = false;
            s_engine = null;
            s_engineFolders = null;
        }
    }

    /// <summary>
    /// The first candidates (at most max) that cover all of the pinyin. Rime's commit text is the highlighted candidate
    /// followed by the input it leaves, so a candidate covers everything when the commit text is just the candidate.
    /// The script translator lists longer matches first, so the scan ends at the first candidate that leaves input;
    /// the index of each result is then also its index in Rime's list.
    /// </summary>
    private List<string> Covering(string pinyin, int max)
    {
        var found = new List<string>();
        RimeClearComposition(_session);
        if (RimeSetInput(_session, pinyin) == 0) return found;
        for (var i = 0; found.Count < max; i++)
        {
            if (i > 0) s_highlight(_session, (nuint)i);
            var context = new RimeContext { DataSize = sizeof(RimeContext) - sizeof(int) };
            if (RimeGetContext(_session, &context) == 0) break;
            try
            {
                var menu = context.Menu;
                // Highlighting past the last candidate stays on the last one.
                if (menu.NumCandidates == 0 || menu.PageNo * menu.PageSize + menu.HighlightedCandidateIndex != i) break;
                var text = Marshal.PtrToStringUTF8(menu.Candidates[menu.HighlightedCandidateIndex].Text);
                if (string.IsNullOrEmpty(text) || text != Marshal.PtrToStringUTF8(context.CommitTextPreview)) break;
                found.Add(text);
            }
            finally
            {
                _ = RimeFreeContext(&context);
            }
        }
        return found;
    }

    private void TakeCommit()
    {
        var commit = new RimeCommit { DataSize = sizeof(RimeCommit) - sizeof(int) };
        if (RimeGetCommit(_session, &commit) != 0) _ = RimeFreeCommit(&commit);
    }

    private void Remember(CacheEntry entry)
    {
        if (_cache.Remove(entry.Pinyin, out var old)) _recent.Remove(old);
        _cache[entry.Pinyin] = _recent.AddFirst(entry);
        if (_recent.Count <= CacheSize) return;
        _cache.Remove(_recent.Last!.Value.Pinyin);
        _recent.RemoveLast();
    }

    /// <param name="Complete">Texts holds every covering candidate, so a larger max finds no more.</param>
    private readonly record struct CacheEntry(string Pinyin, string[] Texts, bool Complete);

    private bool OpenSession()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            try
            {
                _session = RimeCreateSession();
                if (_session != 0 && RimeSelectSchema(_session, SchemaId) != 0 && Covering("ni", 1).Count > 0)
                {
                    RimeClearComposition(_session);
                    Volatile.Write(ref _ready, true);
                    return true;
                }
                Diagnostics.Log.Warn($"Rime の {SchemaId} で変換できません (辞書を読み込めなかった)。");
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"Rime を使えませんでした: {ex.Message}");
            }
            if (_session != 0) _ = RimeDestroySession(_session);
            _session = 0;
            return false;
        }
    }

    private static bool StartEngine(string libraryDirectory, string sharedDataDirectory, string userDataDirectory)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            Load(libraryDirectory);
            Directory.CreateDirectory(userDataDirectory);
            var traits = new RimeTraits
            {
                DataSize = sizeof(RimeTraits) - sizeof(int),
                SharedDataDir = Utf8(sharedDataDirectory),
                UserDataDir = Utf8(userDataDirectory),
                DistributionName = Utf8("Meltype"),
                DistributionCodeName = Utf8("meltype"),
                DistributionVersion = Utf8(Config.ProjectInfo.CoreVersion),
                AppName = Utf8("rime.meltype"),
                MinLogLevel = 2, // errors only
                LogDir = Utf8(""), // standard error only, no log files
            };
            if (!s_setUp)
            {
                RimeSetup(&traits);
                s_setUp = true;
            }
            RimeInitialize(&traits);
            s_initialized = true;
            // Deploys (compiles the dictionaries) only when the data changed since the last time: slow the first time.
            if (RimeStartMaintenance(0) != 0) RimeJoinMaintenanceThread();
            Diagnostics.Log.Info($"Rime を起動しました ({watch.ElapsedMilliseconds}ms)。");
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"Rime を使えませんでした: {ex.Message}");
            return false;
        }
    }

    // Never freed: Rime keeps some of these pointers (the log's app name) for the life of the process.
    private static nint Utf8(string text) => Marshal.StringToCoTaskMemUTF8(text);

    private static string? FindLibrary(string directory)
    {
        string[] names = OperatingSystem.IsWindows() ? ["rime.dll"]
            : OperatingSystem.IsMacOS() ? ["librime.1.dylib", "librime.dylib"]
            : ["librime.so.1", "librime.so"];
        return names.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
    }

    private static void Load(string directory)
    {
        if (s_highlight != null) return;
        if (s_library == 0)
        {
            var path = FindLibrary(directory) ?? throw new DllNotFoundException($"{directory} に librime がありません。");
            s_library = NativeLibrary.Load(path);
            NativeLibrary.SetDllImportResolver(typeof(RimeConverter).Assembly, (name, _, _) => name == Library ? s_library : 0);
        }
        // highlight_candidate has no flat export: it is the 96th function after data_size in RimeApi (rime_api.h).
        var api = rime_get_api();
        var offset = 96 * sizeof(nint);
        if (api == 0 || sizeof(int) + *(int*)api < offset + sizeof(nint)) throw new EntryPointNotFoundException("librime に highlight_candidate がありません (1.10 以降が要る)。");
        s_highlight = (delegate* unmanaged<nuint, nuint, int>)*(nint*)((byte*)api + offset);
    }

    private const string Library = "rime";

    [LibraryImport(Library)] private static partial nint rime_get_api();
    [LibraryImport(Library)] private static partial void RimeSetup(RimeTraits* traits);
    [LibraryImport(Library)] private static partial void RimeInitialize(RimeTraits* traits);
    [LibraryImport(Library)] private static partial void RimeFinalize();
    [LibraryImport(Library)] private static partial int RimeStartMaintenance(int fullCheck);
    [LibraryImport(Library)] private static partial void RimeJoinMaintenanceThread();
    [LibraryImport(Library)] private static partial nuint RimeCreateSession();
    [LibraryImport(Library)] private static partial int RimeDestroySession(nuint session);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] private static partial int RimeSelectSchema(nuint session, string schemaId);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] private static partial int RimeSetInput(nuint session, string input);
    [LibraryImport(Library)] private static partial void RimeClearComposition(nuint session);
    [LibraryImport(Library)] private static partial int RimeGetContext(nuint session, RimeContext* context);
    [LibraryImport(Library)] private static partial int RimeFreeContext(RimeContext* context);
    [LibraryImport(Library)] private static partial int RimeSelectCandidate(nuint session, nuint index);
    [LibraryImport(Library)] private static partial int RimeGetCommit(nuint session, RimeCommit* commit);
    [LibraryImport(Library)] private static partial int RimeFreeCommit(RimeCommit* commit);

    // The structs of rime_api.h (librime 1.17). data_size is the size after itself; Bool is int.
    private struct RimeTraits
    {
        public int DataSize;
        public nint SharedDataDir, UserDataDir, DistributionName, DistributionCodeName, DistributionVersion, AppName, Modules;
        public int MinLogLevel;
        public nint LogDir, PrebuiltDataDir, StagingDir;
    }

    private struct RimeComposition
    {
        public int Length, CursorPos, SelStart, SelEnd;
        public nint Preedit;
    }

#pragma warning disable CS0649 // filled by librime
    private struct RimeCandidate
    {
        public nint Text, Comment, Reserved;
    }
#pragma warning restore CS0649

    private struct RimeMenu
    {
        public int PageSize, PageNo, IsLastPage, HighlightedCandidateIndex, NumCandidates;
        public RimeCandidate* Candidates;
        public nint SelectKeys;
    }

    private struct RimeContext
    {
        public int DataSize;
        public RimeComposition Composition;
        public RimeMenu Menu;
        public nint CommitTextPreview, SelectLabels;
    }

    private struct RimeCommit
    {
        public int DataSize;
        public nint Text;
    }
}
