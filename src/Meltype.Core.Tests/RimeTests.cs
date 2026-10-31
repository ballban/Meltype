// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ballban

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// Pinyin conversion with the real librime and rime-ice (native/rime/Get-Rime.ps1). Runs only when MELTYPE_RIME points at
/// native/rime/bin. Rime learns into a folder next to the test binary, never the user's.
/// </summary>
internal static class RimeTests
{
    // librime is one engine per process, so the tests share one converter.
    private static readonly Lazy<RimeConverter?> Shared = new(Start);

    private static RimeConverter? Start()
    {
        var root = Environment.GetEnvironmentVariable("MELTYPE_RIME");
        if (string.IsNullOrEmpty(root)) return null;
        var user = Path.Combine(AppContext.BaseDirectory, "rime-test-user");
        // Forget what earlier runs learned; keep the compiled dictionaries so only the first run deploys.
        if (Directory.Exists(user))
        {
            foreach (var db in Directory.GetDirectories(user, "*.userdb")) Directory.Delete(db, recursive: true);
        }
        var rime = new RimeConverter(root, Path.Combine(root, "data"), user);
        Assert.True(rime.WhenReady.Wait(TimeSpan.FromMinutes(5)) && rime.WhenReady.Result, "Rime deploys the dictionaries in MELTYPE_RIME");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => rime.Dispose();
        return rime;
    }

    [Test]
    public static void Rime_ConvertsASentence()
    {
        if (Shared.Value is not { } rime) return;
        var candidates = rime.Candidates("jintiantianqihenhao", 5);
        Assert.Equal("今天天气很好", candidates.Count > 0 ? candidates[0] : null);
    }

    [Test]
    public static void Rime_ReturnsOnlyCandidatesForAllOfThePinyin()
    {
        if (Shared.Value is not { } rime) return;
        Assert.Equal("你好", string.Join(" ", rime.Candidates("nihao", 1)));
        var candidates = rime.Candidates("nihao", 9);
        Assert.True(candidates.Count > 1, $"a larger max is not cut to the earlier answer: {string.Join(" ", candidates)}");
        Assert.True(candidates.All(c => !c.Any(char.IsAsciiLetter)), $"no candidate leaves pinyin: {string.Join(" ", candidates)}");
        Assert.True(!candidates.Contains("你") && !candidates.Contains("尼"), $"a candidate for part of the pinyin is not one: {string.Join(" ", candidates)}");

        Assert.Equal(0, rime.Candidates("nihaoi", 9).Count, "i cannot start a syllable: 你好 covers only nihao");
        Assert.Equal(0, rime.Candidates("zhongguox", 9).Count, "x is not a syllable; the last syllable is not completed");
        Assert.Equal(0, rime.Candidates("wsm", 9).Count, "no abbreviations");
    }

    [Test]
    public static void Rime_LearnRaisesTheCommittedCandidate()
    {
        if (Shared.Value is not { } rime) return;
        var before = rime.Candidates("shiyan", 5);
        Assert.True(before.Count >= 3, string.Join(" ", before));
        var chosen = before[2];

        rime.Learn("shiyan", "不是候选");
        Assert.Equal(string.Join(" ", before), string.Join(" ", rime.Candidates("shiyan", 5)), "text that is not a candidate teaches nothing");

        rime.Learn("shiyan", chosen);
        var after = rime.Candidates("shiyan", 5);
        Assert.Equal(chosen, after.Count > 0 ? after[0] : null, $"before: {string.Join(" ", before)}");
        Assert.True(after.ToHashSet().SetEquals(before), $"learning only reorders: {string.Join(" ", before)} → {string.Join(" ", after)}");
    }

    [Test]
    public static void Rime_TypingConvertsThroughTheController() => ChineseTests.WithChinese(() =>
    {
        if (Shared.Value is not { } rime) return;
        var k = new CompositionTests.Keyboard(chinese: rime);
        k.Type("nihao");
        Assert.Equal("nihao", k.Showing);
        k.Type(" \n");
        Assert.Equal("你好", k.Host.Document);

        k = new CompositionTests.Keyboard(live: true, chinese: rime);
        k.Type("jintiantianqihenhao");
        Assert.Equal("今天天气很好", k.Showing);
        k = new CompositionTests.Keyboard(live: true, chinese: rime);
        k.Type("nihaozh");
        Assert.Equal("你好zh", k.Showing);
    });
}
