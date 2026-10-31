// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ballban

using Meltype.Composition;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>Korean (Dubeolsik) typed in the same romaji layout as Japanese and English, without switching.</summary>
internal static class KoreanTests
{
    private static DubeolsikComposer Compose(string keys)
    {
        var composer = new DubeolsikComposer();
        foreach (var c in keys) Assert.True(composer.Feed(c), $"{c} is a Korean key");
        return composer;
    }

    [Test]
    public static void Dubeolsik_JoinsKeysLikeAKoreanIme()
    {
        Assert.Equal("안녀", Compose("dkssu").Text, "a final moves to the next syllable before a vowel");
        Assert.Equal("안녕", Compose("dkssud").Text);
        Assert.Equal("값이", Compose("rkqtdl").Text, "a double final keeps its first consonant");
        Assert.Equal("있다", Compose("dlTek").Text, "Shift types the double consonant");
        Assert.Equal("광", Compose("rhkd").Text, "compound vowel");
        var waiting = Compose("dkss");
        Assert.Equal("안ㄴ", waiting.Text);
        Assert.True(waiting.EndsWithLoneConsonant && !waiting.HasStrayJamo, "a consonant may still wait for its vowel");
        Assert.True(Compose("rs").HasStrayJamo, "two consonants without a vowel cannot join");
        Assert.True(Compose("k").EndsWithLoneVowel, "a vowel without a consonant cannot start a syllable");
        Assert.True(!new DubeolsikComposer().Feed('A'), "Shift only changes Q W E R T O P");
    }

    [Test]
    public static void Korean_TypedWithoutSwitching()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("dkssudgktpdy");
        Assert.Equal("안녕하세요", k.Showing);
        k.Type("\b");
        Assert.Equal("안녕하셍", k.Showing, "Backspace removes one key; the ㅇ is a final until a vowel follows");
        k.Type("y\n");
        Assert.Equal("안녕하세요", k.Host.Document);

        var space = new CompositionTests.Keyboard();
        space.Type("rkatkgkqslek ");
        Assert.Equal("감사합니다 ", space.Host.Document, "Space commits the Hangul and a space");
    }

    [Test]
    public static void Korean_PunctuationStaysAscii()
    {
        // Like after English, Korean keeps ASCII punctuation: in the composition, after committed Hangul, and after Hangul before the caret
        var k = new CompositionTests.Keyboard();
        k.Type("dkssud,dkssud.");
        Assert.Equal("안녕,안녕.", k.Showing);
        k = new CompositionTests.Keyboard();
        k.Type("dkssud\n,");
        Assert.Equal(",", k.Showing);
        k = new CompositionTests.Keyboard();
        k.Host.PrecedingText = "안녕";
        k.Type(".");
        Assert.Equal(".", k.Showing);
    }

    [Test]
    public static void Korean_FollowsEnglishWordAsParticle()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("githubdptj");
        Assert.Equal("github에서", k.Showing);
        k = new CompositionTests.Keyboard();
        k.Type("reactfmf");
        Assert.Equal("react를", k.Showing);
        // Hangul with an uncommon syllable (샐르) that is not a particle or 하다 ending is a misread English word
        k = new CompositionTests.Keyboard();
        k.Type("pythontoffmpeg");
        Assert.Equal("pythonとffmpeg", k.Showing);
    }

    [Test]
    public static void Korean_WordTouchesJapaneseAndEnglishWithoutSpaces()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("dhsmftennkigood");
        Assert.Equal("오늘てんきgood", k.Showing, "Korean, Japanese, then English");
        k = new CompositionTests.Keyboard();
        k.Type("tennkidhsmf");
        Assert.Equal("てんき오늘", k.Showing, "Japanese before Korean");
        k = new CompositionTests.Keyboard();
        k.Type("gooddhsmf");
        Assert.Equal("good오늘", k.Showing, "English before Korean");
    }

    [Test]
    public static void Korean_ContextReadsRomajiKeysAsKorean()
    {
        // go reads as romaji (ご) and English, so it is Korean (해) only after Hangul
        var k = new CompositionTests.Keyboard();
        k.Type("go");
        Assert.Equal("go", k.Showing, "no Korean context");

        k = new CompositionTests.Keyboard();
        k.Host.PrecedingText = "안녕 ";
        k.Type("go");
        Assert.Equal("해", k.Showing, "Hangul before the caret");

        k = new CompositionTests.Keyboard();
        k.Type("tkfkd ");
        Assert.Equal("사랑 ", k.Host.Document);
        k.Type("go");
        Assert.Equal("해", k.Showing, "Hangul just committed");
    }

    [Test]
    public static void Korean_F10TeachesTheKeysAsEnglish()
    {
        var memory = new LanguageMemory(null);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("thx");
            Assert.Equal("솥", k.Showing);
            k.Press(VirtualKeys.F10);
            Assert.Equal("thx", k.Showing, "the first F10 shows the typed keys");
            k.Type("\n");
            Assert.Equal(true, memory.Get("thx"), "F10 over Hangul teaches English");

            var next = new CompositionTests.Keyboard(languages: memory);
            next.Type("thx");
            Assert.Equal("thx", next.Showing);
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }
}
