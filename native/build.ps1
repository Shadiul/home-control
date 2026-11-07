# Builds a release copy of Home Control (self-contained: .NET and the Windows App SDK are included).
#   .\build.ps1        -> %LOCALAPPDATA%\Temp\LEDControlBuild\publish\HomeControl.exe
# Then run .\install.ps1 to install it.
$ErrorActionPreference = "Stop"

$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet-sdk\dotnet.exe"   # per-user .NET SDK
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$out = Join-Path $env:LOCALAPPDATA "Temp\LEDControlBuild\publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

& $dotnet publish (Join-Path $PSScriptRoot "LedControl.csproj") -c Release -r win-x64 --self-contained `
    -p:PublishReadyToRun=true -o $out -nologo -v q
if ($LASTEXITCODE) { exit $LASTEXITCODE }

# The Windows App SDK ships an AI runtime (ONNX Runtime + DirectML) this app never loads.
foreach ($f in "onnxruntime.dll", "DirectML.dll") { Remove-Item (Join-Path $out $f) -ErrorAction SilentlyContinue }
Write-Output "Built: $out\HomeControl.exe"
