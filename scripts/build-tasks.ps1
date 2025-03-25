param (
    [string]$msbuildEngine = "dotnet"
)

Set-StrictMode -version 3.0
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "../eng/build-utils.ps1")

$buildTool = InitializeBuildTool

# Build Toolset package
$projectFile = Join-Path $PSScriptRoot "../src/NuGet/Microsoft.Net.Compilers.Toolset/AnyCpu/Microsoft.Net.Compilers.Toolset.Package.csproj"
Exec-Command $buildTool.Path "$($buildTool.Command) -v:m -m -restore -t:Pack -p:Configuration=Debug $projectFile"

# Extract the package
$packagesDir = Join-Path $PSScriptRoot "../artifacts/packages/Debug/Shipping"
$packageOutput = Join-Path $packagesDir "Microsoft.Net.Compilers.Toolset"
if (Test-Path $packageOutput) { Remove-Item -Recurse $packageOutput } else { New-Item -ItemType Directory -Path $packageOutput }
Expand-Archive -Path (Join-Path $packagesDir "Microsoft.Net.Compilers.Toolset.*.nupkg") -DestinationPath $packageOutput
