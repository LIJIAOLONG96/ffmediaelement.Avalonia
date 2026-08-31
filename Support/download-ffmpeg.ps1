param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\external\ffmpegs")
)

$ErrorActionPreference = "Stop"
$releaseTag = "autobuild-2026-07-31-14-10"
$releaseBaseUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$releaseTag"

$assets = @(
    @{
        Rid = "win-x64"
        File = "ffmpeg-n7.1.5-12-g1fdbca85aa-win64-lgpl-shared-7.1.zip"
        Sha256 = "0f376f96fb38554ccefb1b2ae9c7c6a7b351f0e60a372b38262c320e8392c5d0"
    },
    @{
        Rid = "win-arm64"
        File = "ffmpeg-n7.1.5-12-g1fdbca85aa-winarm64-lgpl-shared-7.1.zip"
        Sha256 = "d4c07a990ae4a0b185481cba63b2ff1b621fbec39c0c5c8d9b043f5efacfd09d"
    },
    @{
        Rid = "linux-x64"
        File = "ffmpeg-n7.1.5-12-g1fdbca85aa-linux64-lgpl-shared-7.1.tar.xz"
        Sha256 = "f5f0ad52c6ee28a222eb10838c231469a10ad325f84063d3bc0aadf08164b3ec"
    },
    @{
        Rid = "linux-arm64"
        File = "ffmpeg-n7.1.5-12-g1fdbca85aa-linuxarm64-lgpl-shared-7.1.tar.xz"
        Sha256 = "28d2c354ad6cc360db0e932598f1cf5845887a1adc46415ded91baf1ca82a53b"
    }
)

$downloadDirectory = Join-Path $OutputDirectory ".downloads"
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

foreach ($asset in $assets) {
    $archivePath = Join-Path $downloadDirectory $asset.File
    $assetUrl = "$releaseBaseUrl/$($asset.File)"

    $archiveIsValid = (Test-Path $archivePath) -and
        ((Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $asset.Sha256)
    if (-not $archiveIsValid) {
        Write-Host "Downloading $($asset.Rid): $($asset.File)"
        & curl.exe --location --fail --retry 5 --continue-at - --output $archivePath $assetUrl
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to download $($asset.File)."
        }
    }

    $actualHash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $asset.Sha256) {
        throw "SHA-256 mismatch for $($asset.File). Expected $($asset.Sha256), got $actualHash."
    }

    $stagingDirectory = Join-Path $downloadDirectory "$($asset.Rid)-extract"
    if (Test-Path $stagingDirectory) {
        Remove-Item $stagingDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    if ($asset.File.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -Path $archivePath -DestinationPath $stagingDirectory -Force
    }
    else {
        & tar -xf $archivePath -C $stagingDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to extract $($asset.File)."
        }
    }

    $packageRoot = Get-ChildItem $stagingDirectory -Directory | Select-Object -First 1
    if ($null -eq $packageRoot) {
        throw "Unable to locate the extracted package root for $($asset.File)."
    }

    $targetDirectory = Join-Path $OutputDirectory $asset.Rid
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

    $sourceDirectoryNames = if ($asset.Rid.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) {
        Get-ChildItem $targetDirectory -File |
            Where-Object { $_.Name -match "(\.def|\.lib|\.dll\.a)$" } |
            Remove-Item -Force
        @("bin")
    }
    else {
        Get-ChildItem $targetDirectory -File |
            Where-Object { $_.Name -match "^lib.*\.so(\..*)?$" } |
            Remove-Item -Force
        @("bin", "lib")
    }

    foreach ($sourceDirectoryName in $sourceDirectoryNames) {
        $sourceDirectory = Join-Path $packageRoot.FullName $sourceDirectoryName
        if (Test-Path $sourceDirectory) {
            $sourceFiles = Get-ChildItem $sourceDirectory -File
            if ($asset.Rid.StartsWith("linux-", [StringComparison]::OrdinalIgnoreCase) -and $sourceDirectoryName -eq "lib") {
                $sourceFiles = $sourceFiles | Where-Object { $_.Name -match "^lib.*\.so\.\d+$" }
            }

            $sourceFiles | Copy-Item -Destination $targetDirectory -Force
        }
    }

    $license = Get-ChildItem $packageRoot.FullName -File |
        Where-Object { $_.Name -match "^(LICENSE|COPYING)" } |
        Select-Object -First 1
    if ($null -ne $license) {
        Copy-Item $license.FullName (Join-Path $targetDirectory "FFMPEG-LICENSE.txt") -Force
    }

    @(
        "FFmpeg 7.1.5 shared LGPL build"
        "RID: $($asset.Rid)"
        "Source: $assetUrl"
        "SHA256: $($asset.Sha256)"
    ) | Set-Content (Join-Path $targetDirectory "VERSION.txt") -Encoding ascii

    Remove-Item $stagingDirectory -Recurse -Force
    Write-Host "Installed $($asset.Rid) to $targetDirectory"
}

Write-Host "FFmpeg shared libraries are ready in $OutputDirectory"