[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$versionFile = Join-Path $repositoryRoot "VERSION"
$version = (Get-Content -LiteralPath $versionFile -Raw).Trim()

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VERSION is empty."
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio Installer could not be found."
}

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
    Select-Object -First 1
if (-not $msbuild) {
    throw "MSBuild could not be found."
}

$solution = Join-Path $repositoryRoot "TraceForge.slnx"
$appProject = Join-Path $repositoryRoot "src\TraceForge.App\TraceForge.App.csproj"
$testProject = Join-Path $repositoryRoot "tests\TraceForge.Application.Tests\TraceForge.Application.Tests.csproj"
$publishDirectory = Join-Path $repositoryRoot "src\TraceForge.App\bin\$Configuration\net10.0-windows10.0.26100.0\win-x64\publish"
$distributionDirectory = Join-Path $repositoryRoot "artifacts\dist"
$configurationSuffix = if ($Configuration -eq "Release") { "" } else { "-debug" }
$archivePath = Join-Path $distributionDirectory "TraceForge-$version$configurationSuffix-win-x64.zip"
$checksumPath = "$archivePath.sha256"

Push-Location $repositoryRoot
try {
    & $msbuild $solution /restore /m "/p:Configuration=$Configuration" /p:Platform=x64 /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Solution build failed with exit code $LASTEXITCODE."
    }

    dotnet test $testProject -c $Configuration --no-restore --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed with exit code $LASTEXITCODE."
    }

    $safePublishRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "src\TraceForge.App\bin")) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $publishDirectory.StartsWith($safePublishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish path is outside the application build output."
    }
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }

    & $msbuild $appProject /t:Publish "/p:Configuration=$Configuration" /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed with exit code $LASTEXITCODE."
    }

    $appExecutable = Join-Path $publishDirectory "TraceForge.App.exe"
    $agentExecutable = Join-Path $publishDirectory "TraceForge.Agent.exe"
    if (-not (Test-Path -LiteralPath $appExecutable) -or -not (Test-Path -LiteralPath $agentExecutable)) {
        throw "Publish output is incomplete."
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot "README.md") -Destination (Join-Path $publishDirectory "README.md")
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs\V1_SCOPE.md") -Destination (Join-Path $publishDirectory "RELEASE_SCOPE.md")

    New-Item -ItemType Directory -Path $distributionDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    if (Test-Path -LiteralPath $checksumPath) {
        Remove-Item -LiteralPath $checksumPath -Force
    }

    Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
    Set-Content -LiteralPath $checksumPath -Value "$hash  $([System.IO.Path]::GetFileName($archivePath))" -Encoding ascii

    Write-Host "Release archive: $archivePath"
    Write-Host "SHA-256: $hash"
}
finally {
    Pop-Location
}
