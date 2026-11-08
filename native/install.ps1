# Installs Home Control for the current user (no admin needed):
#   - copies the build to %LOCALAPPDATA%\Programs\Home Control (replacing any earlier version)
#   - adds a Start menu shortcut
#   - lists it in Settings > Apps > Installed apps, with Uninstall
#   - replaces "LED Control" (the app's name before 2.2): removes its files and entries, and moves its
#     settings folder and "Start with Windows" entry over
#
#   .\install.ps1           install or update
#   .\install.ps1 -Remove   uninstall (settings in %APPDATA%\Home Control are kept)
param([switch]$Remove)
$ErrorActionPreference = "Stop"

$name = "Home Control"
$oldName = "LED Control"
$src = Join-Path $env:LOCALAPPDATA "Temp\HomeControlBuild\publish"
$programs = [Environment]::GetFolderPath("Programs")
$uninstallRoot = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

$target = Join-Path $env:LOCALAPPDATA "Programs\$name"
$exe = Join-Path $target "HomeControl.exe"
$shortcut = Join-Path $programs "$name.lnk"
$uninstallKey = Join-Path $uninstallRoot $name

function Stop-App {
    # HomeControl: this app. LEDControl / "LED Control": its earlier native and Python versions.
    foreach ($n in "HomeControl", "LEDControl", "LED Control") {
        Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object {
            $_.Kill()
            [void]$_.WaitForExit(5000)
        }
    }
    Start-Sleep -Milliseconds 500
}

function Remove-Folder($path) {
    for ($i = 0; $i -lt 10; $i++) {
        try {
            if (Test-Path $path) { Remove-Item $path -Recurse -Force }
            return
        } catch {
            Start-Sleep -Milliseconds 500   # antivirus or a just-closed process can hold files briefly
        }
    }
    throw "Couldn't remove $path because a file is still in use."
}

function Remove-Entries($appName) {
    Remove-Item (Join-Path $programs "$appName.lnk") -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $uninstallRoot $appName) -Recurse -ErrorAction SilentlyContinue
    Remove-ItemProperty $runKey -Name $appName -ErrorAction SilentlyContinue
}

if ($Remove) {
    Stop-App
    Remove-Entries $name
    Remove-Folder $target
    Write-Output "Removed. Your settings are still in %APPDATA%\$name."
    exit
}

if (-not (Test-Path (Join-Path $src "HomeControl.exe"))) { throw "Build first: .\build.ps1" }
$run = Get-ItemProperty $runKey -ErrorAction SilentlyContinue
$hadStartup = ($null -ne $run.$name) -or ($null -ne $run.$oldName)

Stop-App

# Replace the old "LED Control" install, keeping its settings.
$oldSettings = Join-Path $env:APPDATA $oldName
$newSettings = Join-Path $env:APPDATA $name
if ((Test-Path $oldSettings) -and -not (Test-Path (Join-Path $newSettings "config.json"))) {
    if (Test-Path $newSettings) {
        Copy-Item (Join-Path $oldSettings "config.json") $newSettings -ErrorAction SilentlyContinue
    } else {
        Move-Item $oldSettings $newSettings
    }
}
Remove-Entries $oldName
Remove-Folder (Join-Path $env:LOCALAPPDATA "Programs\$oldName")

Remove-Folder $target
Copy-Item $src $target -Recurse

# Start menu shortcut
$ws = New-Object -ComObject WScript.Shell
$s = $ws.CreateShortcut($shortcut)
$s.TargetPath = $exe
$s.WorkingDirectory = $target
$s.IconLocation = "$exe,0"
$s.Description = "Control your light and air conditioners"
$s.Save()

# Settings > Apps entry
$v = (Get-Item $exe).VersionInfo
$version = "$($v.FileMajorPart).$($v.FileMinorPart).$($v.FileBuildPart)"
$sizeKb = [int]((Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
New-Item $uninstallKey -Force | Out-Null
$values = @{
    DisplayName = $name; DisplayVersion = $version; Publisher = "Personal"
    DisplayIcon = "$exe,0"; InstallLocation = $target; UninstallString = "`"$exe`" --uninstall"
}
foreach ($k in $values.Keys) { Set-ItemProperty $uninstallKey -Name $k -Value $values[$k] }
foreach ($k in @{ EstimatedSize = $sizeKb; NoModify = 1; NoRepair = 1 }.GetEnumerator()) {
    New-ItemProperty $uninstallKey -Name $k.Key -Value $k.Value -PropertyType DWord -Force | Out-Null
}

# Keep "Start with Windows" switched on if it was (for this app or under the old name).
if ($hadStartup) { Set-ItemProperty $runKey -Name $name -Value "`"$exe`" --minimized" }

Write-Output "Installed $name $version to $target"
