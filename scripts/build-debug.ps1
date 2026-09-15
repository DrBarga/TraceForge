$ErrorActionPreference = "Stop"
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild was not found." }
& $msbuild "$PSScriptRoot\..\TraceForge.slnx" /restore /m /p:Configuration=Debug /p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
