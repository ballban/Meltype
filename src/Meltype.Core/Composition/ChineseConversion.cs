// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ballban

namespace Meltype.Composition;

/// <summary>Converts pinyin (lowercase letters without separators: jintiantianqi) to Chinese.</summary>
public interface IChineseConverter
{
    /// <summary>False until the engine has loaded its dictionaries. Candidates are empty until then.</summary>
    bool IsReady { get; }

    /// <summary>Up to max candidates that cover the whole pinyin, best first. Empty when none covers it.</summary>
    IReadOnlyList<string> Candidates(string pinyin, int max);

    /// <summary>The user committed text for pinyin, so the engine ranks it higher next time. Best effort.</summary>
    void Learn(string pinyin, string text);
}
