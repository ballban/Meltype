// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ballban

using Meltype.Composition;
using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>Chinese (pinyin) typed in the same romaji layout as Japanese and English, told apart by the words and the text before the caret.</summary>
internal static class ChineseTests
{
    private sealed class FakeChinese : IChineseConverter
    {
        private static readonly Dictionary<string, string[]> Words = new(StringComparer.Ordinal)
        {
            ["nihao"] = ["你好", "拟好"],
            ["jintiantianqihenhao"] = ["今天天气很好"],
            ["women"] = ["我们"],
            ["danshi"] = ["但是", "单式"],
            ["hang"] = ["行", "航"],
            ["long"] = ["龙", "长"],
        };

        private readonly List<(string Pinyin, string Text)> _learned = [];

        public bool IsReady => true;

        public IReadOnlyList<string> Candidates(string pinyin, int max) => Words.TryGetValue(pinyin, out var words) ? words.Take(max).ToList() : [];

        public void Learn(string pinyin, string text)
        {
            lock (_learned) _learned.Add((pinyin, text));
        }

        /// <summary>The controller teaches the engine on the thread pool.</summary>
        public List<(string Pinyin, string Text)> Learned()
        {
            var until = Environment.TickCount64 + 2000;
            while (Environment.TickCount64 < until)
            {
                lock (_learned) if (_learned.Count > 0) return [.. _learned];
                Thread.Sleep(10);
            }
            lock (_learned) return [.. _learned];
        }
    }

    internal static void WithChinese(Action test)
    {
        CompositionTests.Detector.ChineseInput = true;
        try
        {
            test();
        }
        finally
        {
            CompositionTests.Detector.ChineseInput = false;
            CompositionTests.Detector.Memory = null;
        }
    }

    [Test]
    public static void Chinese_PinyinOnlyWordsConvertWithoutContext() => WithChinese(() =>
    {
        var k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Type("nihao");
        Assert.Equal("nihao", k.Showing, "the pinyin shows while typing, like the kana of Japanese");
        k.Type(" ");
        Assert.Equal("你好", k.Host.View!.Clauses![0], "Space converts instead of committing an English word");
        Assert.True(k.Host.View.Candidates.Take(3).SequenceEqual(["你好", "拟好", "nihao"]), string.Join(",", k.Host.View.Candidates));
        k.Type("\n");
        Assert.Equal("你好", k.Host.Document);

        k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Type("jintiantianqihenhao \n");
        Assert.Equal("今天天气很好", k.Host.Document);
    });

    [Test]
    public static void Chinese_JapaneseAndEnglishWordsStayWithoutContext() => WithChinese(() =>
    {
        var k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Type("watashi");
        Assert.Equal("わたし", k.Showing);
        k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Type("la");
        Assert.Equal("ぁ", k.Showing, "one syllable that is not a word does not start Chinese");
        k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Type("women ");
        Assert.Equal("women ", k.Host.Document, "an English word commits with Space");

        // danshi reads Japanese (だんし) and Chinese (但是): Japanese without context, with the Chinese one key away
        k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Type("danshi");
        Assert.Equal("だんし", k.Showing);
        k.Type(" ");
        Assert.True(k.Host.View!.Candidates.Skip(1).Take(2).SequenceEqual(["但是", "单式"]), string.Join(",", k.Host.View.Candidates));
        k.Type(" ");
        Assert.Equal("但是", k.Host.View!.Clauses![0], "the second candidate is Chinese");
    });

    [Test]
    public static void Chinese_ContextReadsAmbiguousPinyinAsChinese() => WithChinese(() =>
    {
        var k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Host.PrecedingText = "这是";
        k.Type("women ");
        Assert.Equal("我们", k.Host.View!.Clauses![0], "simplified Chinese before the caret");
        Assert.Equal("", k.Host.Document);

        // Hanzi such as 很好 also read as Japanese kanji, so the controller remembers that it committed Chinese
        k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Type("nihao \n");
        k.Type("danshi ");
        Assert.Equal("但是", k.Host.View!.Clauses![0], "Chinese just committed");
        Assert.True(k.Host.View.Candidates.Contains("だんし"), "the Japanese reading stays a candidate: " + string.Join(",", k.Host.View.Candidates));

        // Pinyin that is an English word with an unfinished romaji end (long = ぉん + g) converts instead of committing English
        k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Host.PrecedingText = "这是";
        k.Type("long ");
        Assert.Equal("龙", k.Host.View?.Clauses?[0], "Document: " + k.Host.Document);

        k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Host.PrecedingText = "これは";
        k.Type("danshi");
        Assert.Equal("だんし", k.Showing, "kana before the caret keeps Japanese");
    });

    [Test]
    public static void Chinese_LiveConversionShowsChinese() => WithChinese(() =>
    {
        var k = new CompositionTests.Keyboard(live: true, chinese: new FakeChinese());
        k.Type("nihao");
        Assert.Equal("你好", k.Showing);
        k.Type("\n");
        Assert.Equal("你好", k.Host.Document);

        k = new CompositionTests.Keyboard(live: true, chinese: new FakeChinese());
        k.Type("nihaozh");
        Assert.Equal("你好zh", k.Showing, "the syllables before the one being typed stay converted");
    });

    [Test]
    public static void Chinese_ChoiceTeachesOnlyTheChineseEngine() => WithChinese(() =>
    {
        var memory = new LanguageMemory(null);
        var history = new ConversionHistory(null);
        var chinese = new FakeChinese();
        CompositionTests.Detector.Memory = memory;
        var k = new CompositionTests.Keyboard(history: history, languages: memory, chinese: chinese);
        k.Type("danshi  \n");
        Assert.Equal("但是", k.Host.Document);
        Assert.True(chinese.Learned().Contains(("danshi", "但是")), "the Chinese engine learns the choice");
        Assert.Equal(null, memory.Get("danshi"), "not taught as Japanese or English");
        Assert.Equal(null, history.Get("だんし"), "not in the Japanese conversion history");
    });

    [Test]
    public static void Chinese_OffKeepsJapaneseAndEnglish()
    {
        var k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Host.PrecedingText = "这是";
        k.Type("nihao");
        Assert.Equal("にはお", k.Showing);
        k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Host.PrecedingText = "这是";
        k.Type("women");
        Assert.True(k.Showing?.StartsWith("をめ") == true, "Chinese before the caret is kanji context like before: " + k.Showing);
        k = new CompositionTests.Keyboard(chinese: new FakeChinese());
        k.Type("danshi ");
        Assert.True(!k.Host.View!.Candidates.Contains("但是"), string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void Pinyin_EndsWithChineseLooksPastEnglish()
    {
        Assert.True(Pinyin.EndsWithChinese("这是"), "a character only simplified Chinese uses");
        Assert.True(Pinyin.EndsWithChinese("你好 github "), "an English word inside a Chinese sentence");
        Assert.True(!Pinyin.EndsWithChinese("これは"), "kana");
        Assert.True(!Pinyin.EndsWithChinese("这是。これは"), "kana after the Chinese");
        Assert.True(!Pinyin.EndsWithChinese("東京"), "kanji that Japanese also uses tell nothing");
        Assert.True(!Pinyin.EndsWithChinese("안녕"), "Hangul");
        Assert.True(!Pinyin.EndsWithChinese(null), "nothing before the caret");
    }
}
