# Builds a standalone, double-clickable JobTracker.exe - no .NET SDK or
# runtime needs to be installed on the machine that runs it.
#
# Usage: powershell -File publish.ps1
# Output: publish\JobTracker.exe (self-contained, single-file, win-x64)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

dotnet publish "$root\JobTracker\JobTracker.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$root\publish"

Write-Host ""
Write-Host "Built: $root\publish\JobTracker.exe"
Write-Host "Double-click it directly, or run .\make-shortcut.ps1 to put a shortcut on your Desktop."
