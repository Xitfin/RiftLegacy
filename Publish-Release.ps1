param([string]$Version = "1.0.1")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

& "$Root\Build.cmd"
if ($LASTEXITCODE -ne 0) { throw "Backend build failed." }
& "$Root\Build-Launcher.cmd"
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed." }
& npm.cmd run dist:dir
if ($LASTEXITCODE -ne 0) { throw "Electron build failed." }

$ReleaseRoot = Join-Path $Root "distribution\Ready_To_Publish"
$GitHubRoot = Join-Path $ReleaseRoot "GitHub_Release_v$Version"
$PlayerRoot = Join-Path $ReleaseRoot "Give_To_Players"
$UpdateMetadataRoot = Join-Path $Root "distribution\GitHub_Public\update"
New-Item -ItemType Directory -Force -Path $GitHubRoot, $PlayerRoot, $UpdateMetadataRoot | Out-Null

$PackageName = "Rift.Legacy.$Version.zip"
$PackagePath = Join-Path $GitHubRoot $PackageName
if (Test-Path $PackagePath) { Remove-Item -LiteralPath $PackagePath -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $Root "dist\win-unpacked"), $PackagePath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$Package = Get-Item $PackagePath
$Hash = (Get-FileHash $PackagePath -Algorithm SHA256).Hash
$Manifest = [ordered]@{
    product = "Rift Legacy"
    version = $Version
    packageUrl = "https://github.com/Xitfin/RiftLegacy-Updates/releases/download/v$Version/$PackageName"
    sha256 = $Hash
    size = $Package.Length
    entryPoint = "Rift Legacy.exe"
    releaseNotes = @(
        "Automatic installation in a dedicated Rift Legacy folder",
        "Secure GitHub updates with SHA-256 verification",
        "Classic launcher interface restored"
    )
}
$Manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $GitHubRoot "bootstrap.json") -Encoding UTF8
Copy-Item -LiteralPath "$Root\RiftLegacyLauncher.exe" -Destination (Join-Path $PlayerRoot "RiftLegacyLauncher.exe") -Force
Copy-Item -LiteralPath (Join-Path $GitHubRoot "bootstrap.json") -Destination (Join-Path $UpdateMetadataRoot "bootstrap.json") -Force

Write-Host ""
Write-Host "Release ready:" -ForegroundColor Green
Write-Host "  GitHub asset: $PackagePath"
Write-Host "  GitHub manifest: $(Join-Path $GitHubRoot 'bootstrap.json')"
Write-Host "  Player launcher: $(Join-Path $PlayerRoot 'RiftLegacyLauncher.exe')"
Write-Host "  SHA-256: $Hash"
