# AIRERAGE のビルドを itch.io へ上げる（Windows の PowerShell で実行。docs/21_distribution.md）。
# 友達は itch のアプリでゲームを入れておけば、起動のたびに新しい版へ自動で更新される（差分だけを落とす）。
#
# 使い方（リポジトリのいちばん上で）:
#   powershell -ExecutionPolicy Bypass -File tools\release\push_itch.ps1 -BuildDir "Builds\Windows" -Target "あなたのitchのユーザー名/airerage:windows"
#
# 前もって 1 回だけ: butler を入れて（https://itch.io/docs/butler/installing.html）、`butler login` でログインしておく。
param(
    [Parameter(Mandatory = $true)][string]$BuildDir,
    [Parameter(Mandatory = $true)][string]$Target
)
$ErrorActionPreference = "Stop"

if (-not (Get-Command butler -ErrorAction SilentlyContinue)) {
    throw "butler が見つからない。https://itch.io/docs/butler/installing.html の手順で入れて、PATH を通してから実行する。"
}
if (-not (Test-Path (Join-Path $BuildDir "*.exe"))) {
    throw "$BuildDir にゲームの .exe がない。Unity の Build で Windows のビルドを作ってから実行する。"
}

# 版の名前: git のコミット（例 2784b70）と日付。itch のアプリの版の表示に出る。
$commit = (git rev-parse --short HEAD).Trim()
$version = "{0}-{1}" -f (Get-Date -Format "yyyy.MM.dd.HHmm"), $commit

Write-Host "上げる: $BuildDir -> $Target（版 $version）"
butler push $BuildDir $Target --userversion $version --if-changed
if ($LASTEXITCODE -ne 0) {
    throw "butler push が失敗した（終了コード $LASTEXITCODE）。上のメッセージを見る。"
}
butler status $Target
Write-Host "済み。友達の itch のアプリは、次にゲームを起動するときに自動で更新する。"
