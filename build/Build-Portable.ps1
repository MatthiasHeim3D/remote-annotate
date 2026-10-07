[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$clientProject = Join-Path $repositoryRoot 'src\RemoteAnnotate.Client\RemoteAnnotate.Client.csproj'
$publishDirectory = Join-Path $repositoryRoot 'artifacts\publish\client\win-x64'
$portableDirectory = Join-Path $repositoryRoot 'artifacts\portable'

Push-Location $repositoryRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "Nerdbank.GitVersioning tool restore failed with exit code $LASTEXITCODE."
    }

    $Version = (& dotnet nbgv get-version --public-release -v NuGetPackageVersion).Trim()
    if ($LASTEXITCODE -ne 0 -or $Version -notmatch '^\d+\.\d+\.\d+$') {
        throw 'Could not determine the numeric build version from Nerdbank.GitVersioning.'
    }
}
finally {
    Pop-Location
}

# Same self-contained publish as Build-Installer.ps1, so both artifacts ship identical binaries.
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
& dotnet publish $clientProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    '-p:PublicRelease=true' `
    '-p:DebugType=None'
if ($LASTEXITCODE -ne 0) {
    throw "Client publish failed with exit code $LASTEXITCODE."
}

$settings = Get-Content -Raw -LiteralPath (Join-Path $publishDirectory 'appsettings.json') |
    ConvertFrom-Json
if (-not [string]::IsNullOrEmpty($settings.Server.BaseUrl)) {
    throw 'The published appsettings.json must not contain a preconfigured relay URL.'
}

New-Item -ItemType Directory -Path $portableDirectory -Force | Out-Null
$archivePath = Join-Path $portableDirectory "RemoteAnnotate.Client-$Version-x64-Portable.zip"
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

# Zip a single top-level folder so extracting never scatters files into the destination.
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "RemoteAnnotate-portable-$([guid]::NewGuid().ToString('N'))"
$stagingDirectory = Join-Path $stagingRoot 'RemoteAnnotate'
try {
    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    Copy-Item -LiteralPath $publishDirectory -Destination $stagingDirectory -Recurse
    Compress-Archive -LiteralPath $stagingDirectory -DestinationPath $archivePath -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath
$hashPath = "$archivePath.sha256"
"$($hash.Hash)  $(Split-Path -Leaf $archivePath)" |
    Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Output $archivePath
Write-Output $hashPath
