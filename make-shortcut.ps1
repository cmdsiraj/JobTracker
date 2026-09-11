# Creates a Desktop shortcut to the published JobTracker.exe. Run
# publish.ps1 first if publish\JobTracker.exe doesn't exist yet.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = "$root\publish\JobTracker.exe"

if (-not (Test-Path $exe)) {
    Write-Host "publish\JobTracker.exe not found - run .\publish.ps1 first."
    exit 1
}

$shortcutPath = [Environment]::GetFolderPath("Desktop") + "\JobTracker.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = "$root\publish"
$shortcut.IconLocation = $exe
$shortcut.Description = "JobTracker - Gmail-powered job application tracker"
$shortcut.Save()

Write-Host "Created: $shortcutPath"
