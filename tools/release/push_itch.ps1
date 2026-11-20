# Upload the AIRERAGE Windows build to itch.io with butler (see docs/21_distribution.md).
#
# Keep this file ASCII only. Windows PowerShell 5.1 reads a .ps1 without a BOM in the
# system code page (Shift_JIS on Japanese Windows), so any Japanese text here breaks
# the script (CI checks this).
#
# Usage (from the top of the repository):
#   powershell -ExecutionPolicy Bypass -File tools\release\push_itch.ps1 -BuildDir "Builds\Windows" -Target "USER/airerage:windows"
# Preview the file list without uploading:
#   powershell -ExecutionPolicy Bypass -File tools\release\push_itch.ps1 -BuildDir "Builds\Windows" -Target "USER/airerage:windows" -DryRun
#
# Once: install butler (https://itch.io/docs/butler/installing.html) and run "butler login".
param(
    [Parameter(Mandatory = $true)][string]$BuildDir,
    [Parameter(Mandatory = $true)][string]$Target,
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"

if (-not (Get-Command butler -ErrorAction SilentlyContinue)) {
    throw "butler was not found. Install it (https://itch.io/docs/butler/installing.html) and add its folder to PATH, then open a new PowerShell window."
}
if (-not (Test-Path -Path $BuildDir -PathType Container)) {
    throw "Build folder not found: $BuildDir (build the Windows player from Unity into this folder first)."
}
# The whole folder is the game: the .exe alone does not run without *_Data, UnityPlayer.dll and so on.
$exe = Get-ChildItem -Path $BuildDir -Filter "*.exe" -File | Where-Object { $_.Name -ne "UnityCrashHandler64.exe" }
if (-not $exe) {
    throw "No game .exe in $BuildDir. Build the Windows player from Unity into this folder first."
}
$data = Get-ChildItem -Path $BuildDir -Filter "*_Data" -Directory
if (-not $data) {
    throw "No *_Data folder in $BuildDir. Point -BuildDir at the whole build folder, not at the .exe."
}

# Folders that Unity writes next to the game but that must not be shipped.
$ignore = @(
    "--ignore", "*_BurstDebugInformation_DoNotShip",
    "--ignore", "*_BackUpThisFolder_ButDontShipItWithYourGame"
)

if ($DryRun) {
    butler push $BuildDir $Target @ignore --dry-run
    exit $LASTEXITCODE
}

# Version label shown in the itch app: date and the short git commit (if git is available).
$version = Get-Date -Format "yyyy.MM.dd.HHmm"
if (Get-Command git -ErrorAction SilentlyContinue) {
    # Windows PowerShell 5.1 turns redirected stderr of a native command into an error under "Stop".
    $ErrorActionPreference = "Continue"
    $commit = (git rev-parse --short HEAD 2>$null)
    $gitExit = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($gitExit -eq 0 -and $commit) {
        $version = "$version-$($commit.Trim())"
    } else {
        Write-Host "No git commit found; the version is the date only."
    }
}

Write-Host "Pushing $BuildDir to $Target (version $version)"
butler push $BuildDir $Target @ignore --userversion $version
if ($LASTEXITCODE -ne 0) {
    throw "butler push failed (exit code $LASTEXITCODE). See the messages above."
}
butler status $Target
Write-Host "Done. Friends' itch apps will update the game the next time they launch it."
