// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ballban

namespace Meltype.Detection;

/// <summary>
/// Pinyin (Mandarin romanization without tones, ü typed as v) syllables and words from chinese-pinyin.txt, and the text before the caret
/// that shows Chinese is being typed.
/// </summary>
public static class Pinyin
{
    /// <summary>The longest syllable (zhuang).</summary>
    public const int MaxSyllableLength = 6;

    /// <summary>The tier of a syllable (de・zhuang), or null if the letters are not a syllable.</summary>
    public static SyllableTier? Syllable(string letters) => Data.Value.Syllables.TryGetValue(letters, out var tier) ? tier : null;

    /// <summary>Whether the letters start some syllable (zh・xia), so a syllable may still be being typed.</summary>
    public static bool IsSyllablePrefix(string letters) => Data.Value.Prefixes.Contains(letters);

    /// <summary>The tier of a word of two or more syllables written without separators (nihao・jintian), or null if it is not listed.</summary>
    public static SyllableTier? Word(string letters) => Data.Value.Words.TryGetValue(letters, out var tier) ? tier : null;

    /// <summary>Whether the letters split into whole syllables (jintiantianqi).</summary>
    public static bool IsSyllables(string letters)
    {
        if (letters.Length == 0) return false;
        var reachable = new bool[letters.Length + 1];
        reachable[0] = true;
        for (var start = 0; start < letters.Length; start++)
        {
            if (!reachable[start]) continue;
            for (var end = start + 1; end <= Math.Min(letters.Length, start + MaxSyllableLength); end++)
                if (Syllable(letters[start..end]) is not null) reachable[end] = true;
        }
        return reachable[letters.Length];
    }

    /// <summary>A common simplified character that Japanese text does not use (这・们・说).</summary>
    public static bool IsSimplified(char c) => Data.Value.Simplified.Contains(c);

    /// <summary>
    /// Whether the text before the caret is Chinese: going back over ASCII (我用github ), the nearest character that tells the languages apart
    /// is a simplified character. Kana or Hangul closer to the caret means Japanese or Korean. Kanji alone (東京) tells nothing.
    /// </summary>
    public static bool EndsWithChinese(string? text)
    {
        if (text is null) return false;
        var seen = 0;
        for (var i = text.Length - 1; i >= 0 && seen < 32; i--)
        {
            var c = text[i];
            if (c < 0x80) continue;
            seen++;
            if (c is >= '\u3041' and <= '\u30FF' or >= '\uFF66' and <= '\uFF9F' || Dubeolsik.IsHangul(c)) return false;
            if (IsSimplified(c)) return true;
        }
        return false;
    }

    private sealed record Tables(Dictionary<string, SyllableTier> Syllables, HashSet<string> Prefixes, Dictionary<string, SyllableTier> Words, HashSet<char> Simplified);

    private static readonly Lazy<Tables> Data = new(() =>
    {
        var tables = new Tables([], [], [], []);
        foreach (var line in DictionarySource.DataLines(DictionarySource.ReadEmbedded("chinese-pinyin.txt")))
        {
            var items = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (items.Length < 2) continue;
            switch (items[0])
            {
                case "syllable-common": AddSyllables(SyllableTier.Common); break;
                case "syllable-standard": AddSyllables(SyllableTier.Standard); break;
                case "syllable-rare": AddSyllables(SyllableTier.Rare); break;
                case "word-common": AddWords(SyllableTier.Common); break;
                case "word-standard": AddWords(SyllableTier.Standard); break;
                case "simplified":
                    foreach (var run in items.Skip(1)) tables.Simplified.UnionWith(run);
                    break;
            }

            void AddSyllables(SyllableTier tier)
            {
                foreach (var syllable in items.Skip(1))
                {
                    tables.Syllables.TryAdd(syllable, tier);
                    for (var length = 1; length <= syllable.Length; length++) tables.Prefixes.Add(syllable[..length]);
                }
            }

            void AddWords(SyllableTier tier)
            {
                foreach (var word in items.Skip(1)) tables.Words.TryAdd(word, tier);
            }
        }
        return tables;
    });
}
