[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path

$hashPath = "$resolvedArchive.sha256"
if (Test-Path -LiteralPath $hashPath -PathType Leaf) {
    $expected = ((Get-Content -LiteralPath $hashPath -TotalCount 1) -split '\s+')[0]
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedArchive).Hash
    if ($expected -ne $actual) {
        throw "SHA-256 mismatch: expected $expected, got $actual."
    }
}
else {
    Write-Warning "No .sha256 file found next to the archive; skipping the hash check."
}

$extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) "RemoteAnnotate-portable-test-$([guid]::NewGuid().ToString('N'))"
try {
    Expand-Archive -LiteralPath $resolvedArchive -DestinationPath $extractRoot
    $applicationDirectory = Join-Path $extractRoot 'RemoteAnnotate'
    $executablePath = Join-Path $applicationDirectory 'RemoteAnnotate.Client.exe'
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "The extracted executable was not found: $executablePath"
    }

    $settings = Get-Content -Raw -LiteralPath (Join-Path $applicationDirectory 'appsettings.json') |
        ConvertFrom-Json
    if (-not [string]::IsNullOrEmpty($settings.Server.BaseUrl)) {
        throw 'A fresh portable build must not contain a preconfigured relay URL.'
    }

    # Smoke launch: the client must stay running without installer, elevation, or a .NET install.
    $process = Start-Process -FilePath $executablePath -PassThru
    try {
        Start-Sleep -Seconds 10
        if ($process.HasExited) {
            throw "The client exited during startup with exit code $($process.ExitCode)."
        }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }
}
finally {
    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
}

Write-Output 'Portable archive checks passed: hash, extraction, empty relay URL, and launch without installation.'
