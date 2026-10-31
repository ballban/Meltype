// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ballban

using System.Text;

namespace Meltype.Detection;

/// <summary>How common a Hangul syllable is (korean-syllables.txt).</summary>
public enum SyllableTier { Common, Standard, Rare }

/// <summary>
/// The Korean Dubeolsik (2-set) keyboard layout: which key types which jamo, and the rules that join jamo into Hangul syllables.
/// Shift only changes the keys that have a second jamo (Q W E R T O P); other capital letters are not Korean keys.
/// </summary>
public static class Dubeolsik
{
    private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private const string Medials = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    // Index 0 is "no final consonant".
    private const string Finals = "\0ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";
    // Triples: first, second, joined.
    private const string CompoundVowels = "ㅗㅏㅘㅗㅐㅙㅗㅣㅚㅜㅓㅝㅜㅔㅞㅜㅣㅟㅡㅣㅢ";
    private const string DoubleFinals = "ㄱㅅㄳㄴㅈㄵㄴㅎㄶㄹㄱㄺㄹㅁㄻㄹㅂㄼㄹㅅㄽㄹㅌㄾㄹㅍㄿㄹㅎㅀㅂㅅㅄ";

    /// <summary>The jamo the key types, or null if the key is not a Korean key.</summary>
    public static char? Jamo(char key) => key switch
    {
        'q' => 'ㅂ',
        'w' => 'ㅈ',
        'e' => 'ㄷ',
        'r' => 'ㄱ',
        't' => 'ㅅ',
        'y' => 'ㅛ',
        'u' => 'ㅕ',
        'i' => 'ㅑ',
        'o' => 'ㅐ',
        'p' => 'ㅔ',
        'a' => 'ㅁ',
        's' => 'ㄴ',
        'd' => 'ㅇ',
        'f' => 'ㄹ',
        'g' => 'ㅎ',
        'h' => 'ㅗ',
        'j' => 'ㅓ',
        'k' => 'ㅏ',
        'l' => 'ㅣ',
        'z' => 'ㅋ',
        'x' => 'ㅌ',
        'c' => 'ㅊ',
        'v' => 'ㅍ',
        'b' => 'ㅠ',
        'n' => 'ㅜ',
        'm' => 'ㅡ',
        'Q' => 'ㅃ',
        'W' => 'ㅉ',
        'E' => 'ㄸ',
        'R' => 'ㄲ',
        'T' => 'ㅆ',
        'O' => 'ㅒ',
        'P' => 'ㅖ',
        _ => null,
    };

    public static bool IsKey(char key) => Jamo(key) is not null;

    public static bool IsVowel(char jamo) => jamo is >= 'ㅏ' and <= 'ㅣ';

    /// <summary>A precomposed Hangul syllable (가〜힣).</summary>
    public static bool IsSyllable(char c) => c is >= '\uAC00' and <= '\uD7A3';

    /// <summary>A Hangul syllable or a compatibility jamo (ㄱ・ㅏ).</summary>
    public static bool IsHangul(char c) => IsSyllable(c) || c is >= '\u3131' and <= '\u318E';

    /// <summary>
    /// Whether the last character of the text, skipping spaces and ASCII punctuation (안녕하세요. ), is Hangul.
    /// The next input continues Korean there.
    /// </summary>
    public static bool EndsWithHangul(string? text)
    {
        if (text is null) return false;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c < 0x80 && (char.IsPunctuation(c) || char.IsSymbol(c))) continue;
            return IsHangul(c);
        }
        return false;
    }

    internal static bool IsFinal(char consonant) => Finals.IndexOf(consonant, 1) > 0;

    internal static char? CompoundVowel(char first, char second) => Join(CompoundVowels, first, second);

    internal static char? DoubleFinal(char first, char second) => Join(DoubleFinals, first, second);

    /// <summary>Splits a final before a vowel: a double final keeps its first consonant (앉 + ㅏ → 안자), a single one moves whole (안 + ㅕ → 아녀).</summary>
    internal static (char Kept, char Moved) SplitFinal(char final)
    {
        for (var i = 0; i < DoubleFinals.Length; i += 3)
            if (DoubleFinals[i + 2] == final) return (DoubleFinals[i], DoubleFinals[i + 1]);
        return ('\0', final);
    }

    internal static char Syllable(char initial, char medial, char final) =>
        (char)(0xAC00 + (Initials.IndexOf(initial) * Medials.Length + Medials.IndexOf(medial)) * Finals.Length + (final == '\0' ? 0 : Finals.IndexOf(final, 1)));

    private static char? Join(string table, char first, char second)
    {
        for (var i = 0; i < table.Length; i += 3)
            if (table[i] == first && table[i + 1] == second) return table[i + 2];
        return null;
    }

    public static SyllableTier Tier(char syllable) =>
        Tiers.Value.Common.Contains(syllable) ? SyllableTier.Common : Tiers.Value.Standard.Contains(syllable) ? SyllableTier.Standard : SyllableTier.Rare;

    private static readonly Lazy<(HashSet<char> Common, HashSet<char> Standard)> Tiers = new(() =>
    {
        HashSet<char> common = [];
        HashSet<char> standard = [];
        foreach (var line in DictionarySource.DataLines(DictionarySource.ReadEmbedded("korean-syllables.txt")))
        {
            var space = line.IndexOf(' ');
            var set = space < 0 ? null : line[..space] switch { "common" => common, "standard" => standard, _ => null };
            if (set is null) continue;
            foreach (var c in line.AsSpan(space + 1))
                if (IsSyllable(c)) set.Add(c);
        }
        return (common, standard);
    });
}

/// <summary>
/// Joins Dubeolsik keys into Hangul one key at a time, the way a Korean IME does (dkssud → 안녕).
/// A final consonant moves to the next syllable when a vowel follows (dkssu → 안녀).
/// </summary>
public sealed class DubeolsikComposer
{
    private readonly StringBuilder _done = new();
    private int _doneSyllables;
    // The syllable being built. '\0' when absent.
    private char _initial;
    private char _medial;
    private char _final;

    /// <summary>A jamo was left behind without joining a syllable (ㄱㄴ, ㅏ). Later keys cannot fix it.</summary>
    public bool HasStrayJamo { get; private set; }

    /// <summary>The last block is a vowel with no consonant before it. It can never become a syllable.</summary>
    public bool EndsWithLoneVowel => _initial == '\0' && _medial != '\0';

    /// <summary>The last block is a consonant still waiting for its vowel (안녕ㅎ).</summary>
    public bool EndsWithLoneConsonant => _initial != '\0' && _medial == '\0';

    /// <summary>Complete syllables, including the one being built.</summary>
    public int Syllables => _doneSyllables + (_initial != '\0' && _medial != '\0' ? 1 : 0);

    public string Text
    {
        get
        {
            if (_initial == '\0' && _medial == '\0') return _done.ToString();
            var block = _medial == '\0' ? _initial : _initial == '\0' ? _medial : Dubeolsik.Syllable(_initial, _medial, _final);
            return _done.ToString() + block;
        }
    }

    /// <summary>Adds one key. Returns false (and changes nothing) if the key is not a Korean key.</summary>
    public bool Feed(char key)
    {
        if (Dubeolsik.Jamo(key) is not { } jamo) return false;
        if (Dubeolsik.IsVowel(jamo)) AddVowel(jamo);
        else AddConsonant(jamo);
        return true;
    }

    private void AddConsonant(char consonant)
    {
        if (_initial != '\0' && _medial != '\0')
        {
            if (_final == '\0' && Dubeolsik.IsFinal(consonant))
            {
                _final = consonant;
                return;
            }
            if (_final != '\0' && Dubeolsik.DoubleFinal(_final, consonant) is { } joined)
            {
                _final = joined;
                return;
            }
        }
        Flush();
        _initial = consonant;
    }

    private void AddVowel(char vowel)
    {
        if (_final != '\0')
        {
            var (kept, moved) = Dubeolsik.SplitFinal(_final);
            _final = kept;
            Flush();
            _initial = moved;
            _medial = vowel;
            return;
        }
        if (_medial != '\0')
        {
            if (Dubeolsik.CompoundVowel(_medial, vowel) is { } compound)
            {
                _medial = compound;
                return;
            }
            Flush();
        }
        _medial = vowel;
    }

    private void Flush()
    {
        if (_initial != '\0' && _medial != '\0')
        {
            _done.Append(Dubeolsik.Syllable(_initial, _medial, _final));
            _doneSyllables++;
        }
        else if (_initial != '\0' || _medial != '\0')
        {
            _done.Append(_initial != '\0' ? _initial : _medial);
            HasStrayJamo = true;
        }
        _initial = _medial = _final = '\0';
    }
}
