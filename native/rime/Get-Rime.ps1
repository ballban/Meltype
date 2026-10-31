# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 ballban
#
# Downloads librime (Windows x64) and the rime-ice pinyin dictionaries, both pinned and checked by SHA-256,
# into native\rime\bin for Chinese (pinyin) input:
#   bin\rime.dll              librime (no other DLLs needed: the C++ runtime is linked in)
#   bin\data\                 Rime shared data: native\rime\data\*.yaml + cn_dicts\8105.dict.yaml, cn_dicts\base.dict.yaml
#   bin\LICENSE.librime.txt   BSD-3-Clause
#   bin\LICENSE.rime-ice.txt  GPL-3.0
# Set MELTYPE_RIME to bin (or build src\Meltype, which copies bin next to Meltype.exe as rime\). See README.md.
#
#   .\native\rime\Get-Rime.ps1
param(
    [string]$Output = (Join-Path $PSScriptRoot 'bin')
)
$ErrorActionPreference = 'Stop'
# The progress bar makes Invoke-WebRequest very slow on Windows PowerShell 5.1.
$ProgressPreference = 'SilentlyContinue'

$librimeTag = '1.17.0'
$librimeAsset = 'rime-33e7814-Windows-msvc-x64.7z'
$riceCommit = '99662ba01e3affa6a0f009174ca530b887bac3b6'
$riceRaw = "https://raw.githubusercontent.com/iDvel/rime-ice/$riceCommit"
$files = @(
    @{ Name = $librimeAsset; Url = "https://github.com/rime/librime/releases/download/$librimeTag/$librimeAsset"; Sha256 = '7478C7CAA4FF6B37DE86DABA1F7CE4A994A4F5BA24872A820FB2B3A9B01FED15' }
    @{ Name = 'librime-LICENSE'; Url = "https://raw.githubusercontent.com/rime/librime/$librimeTag/LICENSE"; Sha256 = 'F67D27A6D2D586FCFED4B4C886A83747095396A39B6641E18E855086BE2EC400' }
    @{ Name = 'rime-ice-LICENSE'; Url = "$riceRaw/LICENSE"; Sha256 = '3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986' }
    @{ Name = '8105.dict.yaml'; Url = "$riceRaw/cn_dicts/8105.dict.yaml"; Sha256 = '1F9A42B91DEA6982BAEE2551981780271AEFFD78876662B9C9F324E56B37B120' }
    @{ Name = 'base.dict.yaml'; Url = "$riceRaw/cn_dicts/base.dict.yaml"; Sha256 = '551CAD93C2E74C7325599E0D23AFBF71C2D2E57DF9AF3F8F6FA16D87E60290A3' }
)

# Downloaded files are kept here and reused while their hash still matches.
$cache = Join-Path $Output '.downloads'
New-Item -ItemType Directory -Force -Path $cache | Out-Null

function Get-PinnedFile($file) {
    $path = Join-Path $cache $file.Name
    if ((Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $file.Sha256) {
        return $path
    }
    Write-Host "ダウンロード: $($file.Url)"
    $part = "$path.part"
    Invoke-WebRequest -Uri $file.Url -OutFile $part -UseBasicParsing
    $hash = (Get-FileHash -LiteralPath $part -Algorithm SHA256).Hash
    if ($hash -ne $file.Sha256) {
        Remove-Item -LiteralPath $part -Force
        throw "$($file.Name) の SHA-256 が一致しません (期待: $($file.Sha256) / 実際: $hash)。"
    }
    Move-Item -LiteralPath $part -Destination $path -Force
    return $path
}

$paths = @{}
foreach ($file in $files) { $paths[$file.Name] = Get-PinnedFile $file }

# Windows' own tar (libarchive) reads .7z. Git's GNU tar may come first in PATH and cannot, so call it by path.
$extracted = Join-Path $cache 'librime'
if (Test-Path -LiteralPath $extracted) { Remove-Item -LiteralPath $extracted -Recurse -Force }
New-Item -ItemType Directory -Force -Path $extracted | Out-Null
$tar = Join-Path $env:SystemRoot 'System32\tar.exe'
$sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $tar) {
    & $tar -xf $paths[$librimeAsset] -C $extracted 'dist/lib/rime.dll'
}
elseif ($sevenZip) {
    & $sevenZip.Source x -y "-o$extracted" $paths[$librimeAsset] 'dist\lib\rime.dll' | Out-Null
}
else {
    throw '.7z を展開できません (Windows の tar.exe も 7z も見つかりません)。'
}
$dll = Join-Path $extracted 'dist\lib\rime.dll'
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dll)) { throw "$librimeAsset から rime.dll を取り出せませんでした。" }

$data = Join-Path $Output 'data'
if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }
$dicts = Join-Path $data 'cn_dicts'
New-Item -ItemType Directory -Force -Path $dicts | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $Output 'rime.dll') -Force
Copy-Item -Path (Join-Path $PSScriptRoot 'data\*.yaml') -Destination $data
Copy-Item -LiteralPath $paths['8105.dict.yaml'] -Destination (Join-Path $dicts '8105.dict.yaml')
Copy-Item -LiteralPath $paths['base.dict.yaml'] -Destination (Join-Path $dicts 'base.dict.yaml')
Copy-Item -LiteralPath $paths['librime-LICENSE'] -Destination (Join-Path $Output 'LICENSE.librime.txt') -Force
Copy-Item -LiteralPath $paths['rime-ice-LICENSE'] -Destination (Join-Path $Output 'LICENSE.rime-ice.txt') -Force
Remove-Item -LiteralPath $extracted -Recurse -Force

# Rime redeploys only when a .yaml in the data folder is newer than its last build, and copies keep the old time.
$now = Get-Date
Get-ChildItem -LiteralPath $data -Recurse -File | ForEach-Object { $_.LastWriteTime = $now }

Write-Host "librime $librimeTag と rime-ice ($($riceCommit.Substring(0, 7))) の辞書を $Output に置きました。"
Write-Host "使うときは環境変数 MELTYPE_RIME に $Output を指定します (README.md)。"
