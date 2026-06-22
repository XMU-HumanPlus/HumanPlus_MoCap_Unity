param(
    [string]$IsccPath = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

if (-not $IsccPath) {
    $Candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "D:\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $Candidates) {
        if (Test-Path -LiteralPath $candidate) {
            $IsccPath = $candidate
            break
        }
    }
}

if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw "Inno Setup compiler ISCC.exe was not found. Install Inno Setup 6 or pass -IsccPath explicitly."
}

& $IsccPath (Join-Path $PSScriptRoot 'build_installer.iss')
