param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $scriptDir "..")
$nativeProject = Join-Path $root "src\TrayHook.Native\TrayHook.Native.vcxproj"
$appProject = Join-Path $root "src\SystrayWrapDoubler.App\SystrayWrapDoubler.App.csproj"
$uninstallerProject = Join-Path $root "src\SystrayWrapDoubler.Uninstaller\SystrayWrapDoubler.Uninstaller.csproj"
$installerProject = Join-Path $root "src\SystrayWrapDoubler.Installer\SystrayWrapDoubler.Installer.csproj"
$payloadDir = Join-Path $root "src\SystrayWrapDoubler.Installer\Payload"
$artifactsDir = Join-Path $root "artifacts\release"
$publishDir = Join-Path $root "artifacts\publish"

function Assert-UnderRoot([string]$path) {
    $fullPath = [System.IO.Path]::GetFullPath($path).TrimEnd('\')
    $fullRoot = [System.IO.Path]::GetFullPath($root).TrimEnd('\')
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the workspace: $fullPath"
    }
}

function Remove-WorkspaceDirectory([string]$path) {
    Assert-UnderRoot $path
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

function New-CleanDirectory([string]$path) {
    Remove-WorkspaceDirectory $path
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

function Copy-DirectoryContents([string]$source, [string]$destination) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    }
}

function Copy-SourceTree([string]$destination) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $excludedSegments = @(".git", "artifacts", "bin", "obj", "x64", "Payload")
    $excludedFiles = @("*.user", "*.suo")
    $rootFullPath = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'

    Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object {
        $fullName = [System.IO.Path]::GetFullPath($_.FullName)
        $relative = $fullName.Substring($rootFullPath.Length)
        $segments = $relative -split '[\\/]'
        if ($segments | Where-Object { $excludedSegments -contains $_ }) {
            return
        }

        foreach ($pattern in $excludedFiles) {
            if ($_.Name -like $pattern) {
                return
            }
        }

        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

function Find-MSBuild {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "MSBuild.exe was not found. Install Visual Studio Build Tools with the C++ workload."
}

function Invoke-Checked([scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE"
    }
}

Push-Location $root
try {
    Get-Process SystrayWrapDoubler -ErrorAction SilentlyContinue | Stop-Process -Force

    New-CleanDirectory $publishDir
    New-CleanDirectory $artifactsDir
    New-CleanDirectory $payloadDir

    $msbuild = Find-MSBuild
    Invoke-Checked { & $msbuild $nativeProject /p:Configuration=$Configuration /p:Platform=x64 /m }

    $appPublish = Join-Path $publishDir "app"
    $uninstallerPublish = Join-Path $publishDir "uninstaller"
    $installerPublish = Join-Path $publishDir "installer"

    Invoke-Checked { dotnet publish $appProject -c $Configuration -r win-x64 --self-contained true -o $appPublish `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false }

    Invoke-Checked { dotnet publish $uninstallerProject -c $Configuration -r win-x64 --self-contained true -o $uninstallerPublish `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false }

    Copy-DirectoryContents $appPublish $payloadDir
    Copy-Item -LiteralPath (Join-Path $uninstallerPublish "Uninstall.exe") -Destination (Join-Path $payloadDir "Uninstall.exe") -Force
    Copy-Item -LiteralPath (Join-Path $root "src\TrayHook.Native\x64\Release\TrayHook.Native.dll") -Destination (Join-Path $payloadDir "TrayHook.Native.dll") -Force
    Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $payloadDir "README.md") -Force
    Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $payloadDir "LICENSE") -Force
    Copy-Item -LiteralPath (Join-Path $root "RELEASE_NOTES.md") -Destination (Join-Path $payloadDir "RELEASE_NOTES.md") -Force
    Copy-Item -LiteralPath (Join-Path $root "docs") -Destination (Join-Path $payloadDir "docs") -Recurse -Force
    Copy-SourceTree (Join-Path $payloadDir "source")

    Invoke-Checked { dotnet publish $installerProject -c $Configuration -r win-x64 --self-contained true -o $installerPublish `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false }

    $setupExe = Join-Path $installerPublish "SystrayWrapDoublerSetup.exe"
    $releaseSetupExe = Join-Path $artifactsDir "SystrayWrapDoublerSetup.exe"
    Copy-Item -LiteralPath $setupExe -Destination $releaseSetupExe -Force

    $zipPath = Join-Path $artifactsDir "SystrayWrapDoublerSetup.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -LiteralPath $releaseSetupExe -DestinationPath $zipPath -Force

    Write-Host "Release created:"
    Write-Host "  $releaseSetupExe"
    Write-Host "  $zipPath"
}
finally {
    Pop-Location
}
