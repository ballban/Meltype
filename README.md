<p align="center">
  <img src="docs/images/logo.png" alt="Meltype" width="480"><br>
  <sub>Logo by <a href="https://github.com/Crysta1221">@Crysta1221</a></sub>
</p>

<p align="center"><b>A multilingual input method that melts the half-width/full-width wall.</b></p>

<p align="center">
  <a href="https://github.com/ballban/Meltype">This fork</a>
  &nbsp;·&nbsp;
  <a href="https://github.com/yksr-melt/Meltype">Upstream project</a>
  &nbsp;·&nbsp;
  <a href="LICENSE">GPL-3.0 license</a>
</p>

<p align="center">
  <img src="docs/images/demo.svg" alt="Typing kyouhagoogledekensaku produces 今日はgoogleで検索" width="640">
</p>

## About this fork

This repository is a personal fork of [Meltype](https://github.com/yksr-melt/Meltype), an input method that automatically distinguishes Japanese and English while you type.

I added experimental support for Chinese and Korean, together with several improvements to candidate selection and language-aware punctuation:

- Korean input detection using the Dubeolsik keyboard layout.
- Simplified Chinese input from full pinyin through [librime](https://github.com/rime/librime) and the [rime-ice](https://github.com/iDvel/rime-ice) dictionaries.
- Chinese candidates, Chinese context, and Chinese punctuation.
- A setting for half-width punctuation after Chinese text.
- Candidate-first input with numbered choices and remembered language preferences.
- Number-key selection for conversion candidates, including candidate-first choices.

These additions are maintained as a fork and may differ from upstream Meltype.

## Examples

```text
kyouhagoogledekensaku    -> 今日はgoogleで検索
ashitanomeetingwotsuika  -> 明日のmeetingを追加
gksrmf                   -> 한글
nihao                    -> 你好
I want to go to the park -> I want to go to the park
```

Japanese text is converted to kana or kanji, English stays in Latin characters, Korean keystrokes can become Hangul, and Chinese pinyin can become hanzi. Normal English sentences remain unchanged.

## Features

- **Automatic language detection**: Type normally without switching between Japanese and English for every word.
- **Korean and Chinese in the same composition**: Japanese, English, Hangul, and hanzi can appear together in one input.
- **Local conversion**: Detection, Japanese conversion, and the optional Chinese conversion run on the local machine.
- **Candidate learning**: The input method remembers corrections and selected language preferences.
- **Developer-friendly input**: `/commands`, `$skills`, and `@file-names` can be passed through unchanged when typed at the start of a line.
- **Emoji and spelling suggestions**: Common emoji, symbols, and typo corrections are available through conversion candidates.

## Platform status

- **Windows 10/11**: Primary platform and the most thoroughly tested integration.
- **macOS and Linux**: Preview integrations from the upstream project.
- **Core tests**: The platform-independent core can be tested on Windows, macOS, and Linux.

## Installation

This fork currently does not publish separate binary releases. Build it from source to use the fork-specific features.

### Windows

Requirements:

- Windows 10 or 11 (x64 or ARM64)
- .NET 10 SDK
- Microsoft IME for Japanese conversion
- Visual Studio Build Tools with desktop C++ development if you also build the Meltype IME

From the repository root:

```powershell
dotnet build Meltype.sln
powershell -ExecutionPolicy Bypass -File .\Install-Meltype.ps1
```

The installer registers the application and starts it. When the tray icon appears, Meltype is ready.

### Optional Chinese input

Chinese pinyin support uses librime and the pinned rime-ice dictionaries. Download them with:

```powershell
.\native\rime\Get-Rime.ps1
$env:MELTYPE_RIME = (Resolve-Path native\rime\bin).Path
```

The Chinese converter is optional. If librime is not available, Japanese, English, and Korean input continue to work normally.

For packaged upstream builds, see the [upstream releases](https://github.com/yksr-melt/Meltype/releases). Those packages do not include the additions in this fork.

## Basic usage

Meltype can run as a Windows IME or as the Meltype keyboard. The main keys are:

| Key | Action |
|---|---|
| <kbd>Enter</kbd> | Commit the current text |
| <kbd>Space</kbd> | Open or cycle conversion candidates |
| <kbd>1</kbd> - <kbd>9</kbd> | Select a numbered candidate while converting |
| <kbd>F7</kbd> / <kbd>F10</kbd> | Convert to katakana / Latin characters |
| <kbd>半角/全角</kbd> | Switch between Latin and Japanese input |
| <kbd>Ctrl</kbd> + <kbd>半角/全角</kbd> | Temporarily pause Meltype |
| <kbd>Win</kbd> + <kbd>Space</kbd> | Switch between Windows IMEs |

When **candidate-first input** is enabled, Meltype keeps the typed letters visible and shows candidates for the whole input while you type. Press `Space` to commit the selected candidate, `Enter` to commit the typed letters, or a number key to choose a numbered candidate.

For the full list of settings and key bindings, see [docs/USAGE.md](docs/USAGE.md).

## Privacy

Typed text is not sent to a cloud conversion service. Language detection and conversion are performed locally. Settings, learning data, and the user dictionary are stored on the local machine.

The upstream application uses network access for update checks and for the report form opened by the user. See [SECURITY.md](SECURITY.md) for the security policy.

## Development

The platform-independent code is in `src/Meltype.Core`. The main areas are:

| Directory | Contents |
|---|---|
| `src/Meltype.Core/` | Shared detection, composition, conversion, configuration, and diagnostics |
| `src/Meltype.Core.Tests/` | Cross-platform core tests |
| `src/Meltype/` | Windows application, IME integration, UI, and updater |
| `native/rime/` | Optional librime integration and Chinese dictionaries |
| `mac/` | macOS input method |
| `linux/` | Linux IBus and fcitx5 integrations |
| `dictionaries/` | Built-in dictionaries |

Run the core tests with:

```powershell
dotnet run --project src/Meltype.Core.Tests
dotnet run --project src/Meltype.Core.Tests -- --eval
dotnet run --project src/Meltype.Core.Tests -- --repro kyouhagoogle enter
```

On Windows, run the complete integration tests with:

```powershell
dotnet run --project src/Meltype.Tests
```

Before submitting changes, run the repository formatting check:

```powershell
node tools/check-format.mjs origin/main
```

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for the build layout and [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines.

## License and acknowledgments

Meltype is distributed under the [GNU General Public License v3.0 or later](LICENSE).

This fork is based on the work of [Yukishiro](https://github.com/yksr-melt) and the Meltype contributors. The logo was created by [@Crysta1221](https://github.com/Crysta1221).

The optional Chinese input uses:

- [librime](https://github.com/rime/librime), BSD-3-Clause.
- [rime-ice](https://github.com/iDvel/rime-ice), GPL-3.0.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for bundled third-party components.
