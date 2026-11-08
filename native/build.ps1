# Builds a release copy of Home Control (self-contained: .NET and the Windows App SDK are included).
#   .\build.ps1        -> %LOCALAPPDATA%\Temp\HomeControlBuild\publish\HomeControl.exe
# Then run .\install.ps1 to install it.
#
#   -Version 2.5.0     stamps that version instead of the one in HomeControl.csproj
#   -Zip <file.zip>    also packs the app with install.ps1 and "Install Home Control.cmd", as a release download
param([string]$Version, [string]$Zip)
$ErrorActionPreference = "Stop"

$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet-sdk\dotnet.exe"   # per-user .NET SDK
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$out = Join-Path $env:LOCALAPPDATA "Temp\HomeControlBuild\publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$extra = @()
if ($Version) { $extra += "-p:Version=$Version" }
& $dotnet publish (Join-Path $PSScriptRoot "HomeControl.csproj") -c Release -r win-x64 --self-contained `
    -p:PublishReadyToRun=true -o $out -nologo -v q @extra
if ($LASTEXITCODE) { exit $LASTEXITCODE }

# The Windows App SDK ships an AI runtime (ONNX Runtime + DirectML) this app never loads.
foreach ($f in "onnxruntime.dll", "DirectML.dll") { Remove-Item (Join-Path $out $f) -ErrorAction SilentlyContinue }
Write-Output "Built: $out\HomeControl.exe"

if ($Zip) {
    $stage = Join-Path $env:LOCALAPPDATA "Temp\HomeControlBuild\zip"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Copy-Item $out $stage -Recurse
    Copy-Item (Join-Path $PSScriptRoot "install.ps1"), (Join-Path $PSScriptRoot "Install Home Control.cmd") $stage
    $Zip = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Zip)
    New-Item (Split-Path $Zip) -ItemType Directory -Force | Out-Null
    if (Test-Path $Zip) { Remove-Item $Zip }
    Compress-Archive (Join-Path $stage "*") $Zip -CompressionLevel Optimal
    Remove-Item $stage -Recurse -Force
    Write-Output "Packed: $Zip"
}
