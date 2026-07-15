# version 7
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$project = Join-Path $PSScriptRoot "ClaudeUsageTray\ClaudeUsageTray.csproj"
$nugetConfig = Join-Path $PSScriptRoot "NuGet.Config"
$publishRoot = Join-Path $PSScriptRoot "publish"
$output = Join-Path $publishRoot "win-x64"
$executable = Join-Path $output "ClaudeUsageTray.exe"
$processName = "ClaudeUsageTray"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (dotnet exit code $LASTEXITCODE)."
    }
}

function Stop-ClaudeUsageTray {
    $runningProcesses = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)

    if ($runningProcesses.Count -eq 0) {
        return
    }

    foreach ($process in $runningProcesses) {
        $displayPath = $null

        try {
            $displayPath = $process.Path
        }
        catch {
            $displayPath = $null
        }

        if ([string]::IsNullOrWhiteSpace($displayPath)) {
            Write-Host "Closing running Claude Usage Tray process (PID $($process.Id))..."
        }
        else {
            Write-Host "Closing running Claude Usage Tray process: $displayPath"
        }

        Stop-Process -Id $process.Id -Force -ErrorAction Stop

        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "ClaudeUsageTray.exe did not close within 10 seconds. Close it from Task Manager and run the script again."
            }

            Start-Sleep -Milliseconds 200
        }
    }

    Start-Sleep -Milliseconds 500
}

function Remove-DirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [int]$Attempts = 8
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq $Attempts) {
                throw "Could not remove '$Path'. Another program may still be using a file in that folder. Close Claude Usage Tray, File Explorer preview windows, and any antivirus scan using the file, then try again. Original error: $($_.Exception.Message)"
            }

            Start-Sleep -Milliseconds (500 * $attempt)
        }
    }
}

function Publish-SingleFileWithRetry {
    param(
        [int]$Attempts = 3
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $stage = Join-Path $publishRoot (".stage-win-x64-" + [Guid]::NewGuid().ToString("N"))
        $stageExecutable = Join-Path $stage "ClaudeUsageTray.exe"

        Write-Host "Single-file publish attempt $attempt of $Attempts..."

        & dotnet @(
            "publish",
            $project,
            "--configuration", "Release",
            "--runtime", "win-x64",
            "--self-contained", "true",
            "--output", $stage,
            "--no-restore",
            "/p:PublishSingleFile=true",
            "/p:IncludeNativeLibrariesForSelfExtract=true",
            "/p:EnableCompressionInSingleFile=true"
        ) | Out-Host

        $publishExitCode = $LASTEXITCODE

        if (($publishExitCode -eq 0) -and (Test-Path -LiteralPath $stageExecutable -PathType Leaf)) {
            return $stage
        }

        Write-Host "Single-file attempt $attempt failed. Waiting before retrying..." -ForegroundColor Yellow
        Remove-DirectoryWithRetry -Path $stage
        Start-Sleep -Seconds (2 * $attempt)
    }

    return $null
}

function Publish-FolderFallback {
    $stage = Join-Path $publishRoot (".stage-folder-win-x64-" + [Guid]::NewGuid().ToString("N"))
    $stageExecutable = Join-Path $stage "ClaudeUsageTray.exe"

    Write-Host "Publishing a self-contained folder build instead..." -ForegroundColor Yellow

    Invoke-DotNet `
        -Arguments @(
            "publish",
            $project,
            "--configuration", "Release",
            "--runtime", "win-x64",
            "--self-contained", "true",
            "--output", $stage,
            "--no-restore",
            "/p:PublishSingleFile=false"
        ) `
        -FailureMessage "Folder-based application publish failed"

    if (-not (Test-Path -LiteralPath $stageExecutable -PathType Leaf)) {
        throw "The folder-based publish completed without creating '$stageExecutable'."
    }

    return $stage
}

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET SDK was not found. Install a compatible .NET 8 SDK, then run this script again."
    }

    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Project file not found: $project"
    }

    if (-not (Test-Path -LiteralPath $nugetConfig -PathType Leaf)) {
        throw "NuGet configuration not found: $nugetConfig"
    }

    $sdkVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0) {
        throw "The installed .NET SDK could not be selected. Check global.json and the output of 'dotnet --list-sdks'."
    }

    Write-Host "Claude Usage Tray - Windows x64 publisher" -ForegroundColor Cyan
    Write-Host "Using .NET SDK: $sdkVersion"
    Write-Host "NuGet source: https://api.nuget.org/v3/index.json"
    Write-Host ""

    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

    Stop-ClaudeUsageTray

    Write-Host "Restoring Microsoft runtime packs..."
    Invoke-DotNet `
        -Arguments @(
            "restore",
            $project,
            "--runtime", "win-x64",
            "--configfile", $nugetConfig
        ) `
        -FailureMessage "Package restore failed"

    Write-Host ""
    Write-Host "Publishing the self-contained executable..."

    $stageOutput = Publish-SingleFileWithRetry -Attempts 3
    $usedFolderFallback = $false

    if ([string]::IsNullOrWhiteSpace($stageOutput)) {
        Write-Host ""
        Write-Host "The single-file bundler remained locked after three attempts." -ForegroundColor Yellow
        $stageOutput = Publish-FolderFallback
        $usedFolderFallback = $true
    }

    Stop-ClaudeUsageTray
    Remove-DirectoryWithRetry -Path $output

    Move-Item -LiteralPath $stageOutput -Destination $output -Force

    Get-ChildItem -LiteralPath $publishRoot -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like ".stage-*" } |
        ForEach-Object {
            Remove-DirectoryWithRetry -Path $_.FullName
        }

    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Publishing completed without creating '$executable'."
    }

    Write-Host ""
    Write-Host "Publish succeeded:" -ForegroundColor Green
    Write-Host $executable

    if ($usedFolderFallback) {
        Write-Host ""
        Write-Host "Note: Windows locked the single-file bundle, so a folder-based self-contained build was created." -ForegroundColor Yellow
        Write-Host "Keep every file in '$output' together when moving or running the application."
    }
}
catch {
    Write-Host ""
    Write-Host "Publish failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
