# librime と中国語 (拼音) の辞書

中国語の拼音入力 (`nihao` → 你好) は、[librime](https://github.com/rime/librime) (RIME の変換エンジン、BSD-3-Clause) と
[rime-ice (雾凇拼音)](https://github.com/iDvel/rime-ice) の辞書 (GPL-3.0) を使います。
Meltype は `rime.dll` を直接読み込み (`src/Meltype.Core/Composition/RimeConverter.cs`)、拼音の全部をおおう候補だけを使います。

| ファイル | 中身 |
| --- | --- |
| `data/default.yaml` | RIME の全体の設定。使うスキーマは `meltype_pinyin` だけ |
| `data/meltype_pinyin.schema.yaml` | 簡体字の全拼のスキーマ。省略 (`wsm` など)・あいまい音・最後の音節の補完はしない。`ü` は `nv`・`lve`・`ju`・`xu` のほか `nue`・`jv` でも打てる。rime-ice の `rime_ice.schema.yaml` を元にした |
| `data/meltype_pinyin.dict.yaml` | 辞書の定義。rime-ice の `cn_dicts/8105` (漢字) と `cn_dicts/base` (語) を読み込む |
| `Get-Rime.ps1` | librime と rime-ice の辞書を取得し、`data/` と合わせて `bin\` に置く。版を固定し、SHA-256 を確かめる |
| `bin\` | 取得したもの (Git には入れない)。`rime.dll`・`data\`・ライセンス |

## 取得

```powershell
.\native\rime\Get-Rime.ps1
```

取得するもの (どれも版を固定しています。上げるときは `Get-Rime.ps1` の先頭の版と SHA-256 を変えます):

- librime 1.17.0 の Windows (x64, MSVC) 版 `rime-33e7814-Windows-msvc-x64.7z` の `rime.dll`。C++ のランタイムは中に入っているので、ほかの DLL は要りません。
- rime-ice の commit `99662ba01e3affa6a0f009174ca530b887bac3b6` の `cn_dicts/8105.dict.yaml`・`cn_dicts/base.dict.yaml`・`LICENSE`。

一度取得したファイルは `bin\.downloads\` に残し、次からはハッシュが合えば使い回します。
`data/` の `.yaml` を変えたら、もう一度 `Get-Rime.ps1` を実行して `bin\data\` に写します (RIME が辞書を作り直します)。

## 使う

Meltype は、次の順に librime を探します。どちらも `rime.dll` と `data\` があるフォルダーです。見つからなければ、中国語の入力は使えません (これまでと同じ動き)。

1. 環境変数 `MELTYPE_RIME` のフォルダー (例: `native\rime\bin` のフルパス)
2. `Meltype.exe` の隣の `rime\`。`native\rime\bin\rime.dll` があれば、`src\Meltype` のビルドが `bin\` の中身をここに写します (`dotnet publish` では写さないので、配布物には入りません)

```powershell
$env:MELTYPE_RIME = (Resolve-Path native\rime\bin).Path
dotnet run --project src/Meltype.Core.Tests -- RimeTests
```

`RimeTests` は `MELTYPE_RIME` があるときだけ動きます (無ければ何もせずに通ります)。学習はテストの出力フォルダーの `rime-test-user\` に保存します。

RIME が作った辞書 (`build\`) と学習データ (`meltype_pinyin.userdb\`) は `%LOCALAPPDATA%\Meltype\rime\` に保存されます。
初めて起動したとき (と `data\` を変えたとき) は、辞書を作るのに数秒かかります。その間はバックグラウンドで作り、終わるまでは拼音を中国語として読みません。

## ライセンス

- librime: BSD-3-Clause (`bin\LICENSE.librime.txt`)
- rime-ice の辞書: GPL-3.0 (`bin\LICENSE.rime-ice.txt`)。`data/meltype_pinyin.schema.yaml` も rime-ice のスキーマを元にしています
