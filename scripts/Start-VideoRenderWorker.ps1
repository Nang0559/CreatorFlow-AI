[CmdletBinding()]
param(
    [string]$RepositoryPath = 'E:\Projects\CreatorFlow-AI',
    [string]$SqlInstance = 'localhost'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location -LiteralPath $RepositoryPath
. '.\scripts\docker\Import-LocalPilotEnv.ps1'
Import-LocalPilotEnv -Path (Join-Path $RepositoryPath '.env')
if ([string]::IsNullOrWhiteSpace($env:VideoRendering__StorageRoot)) { $env:VideoRendering__StorageRoot = 'C:\CreatorFlowData\Video' }
$adobeRoot = Join-Path $env:ProgramFiles 'Adobe'
if (Test-Path -LiteralPath $adobeRoot) {
    $detectedAerender = Get-ChildItem -LiteralPath $adobeRoot -Directory -Filter 'Adobe After Effects*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'Support Files\aerender.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ($detectedAerender) {
        if (-not (Test-Path -LiteralPath $env:VideoRendering__AerenderExe -PathType Leaf)) { $env:VideoRendering__AerenderExe = $detectedAerender }
        $detectedAfterFx = Join-Path (Split-Path $detectedAerender -Parent) 'AfterFX.exe'
        if (-not (Test-Path -LiteralPath $env:VideoRendering__AfterEffectsExe -PathType Leaf)) { $env:VideoRendering__AfterEffectsExe = $detectedAfterFx }
    }
}
if ([string]::IsNullOrWhiteSpace($env:VideoRendering__FFmpegExe) -or -not (Test-Path -LiteralPath $env:VideoRendering__FFmpegExe -PathType Leaf)) {
    $ffmpegCommand = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($ffmpegCommand) { $env:VideoRendering__FFmpegExe = $ffmpegCommand.Source }
}
if ([string]::IsNullOrWhiteSpace($env:VideoRendering__FFprobeExe) -or -not (Test-Path -LiteralPath $env:VideoRendering__FFprobeExe -PathType Leaf)) -and
    (Test-Path -LiteralPath $env:VideoRendering__FFmpegExe -PathType Leaf)) {
    $probeCandidate = Join-Path (Split-Path $env:VideoRendering__FFmpegExe -Parent) 'ffprobe.exe'
    if (Test-Path -LiteralPath $probeCandidate -PathType Leaf) { $env:VideoRendering__FFprobeExe = $probeCandidate }
}

$required = @('VideoRendering__StorageRoot','VideoRendering__AfterEffectsExe',
    'VideoRendering__AerenderExe','VideoRendering__FFmpegExe')
foreach ($name in $required) {
    $value = [Environment]::GetEnvironmentVariable($name,'Process')
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Thiếu cấu hình trong .env: $name" }
    if ($name -ne 'VideoRendering__StorageRoot' -and -not (Test-Path -LiteralPath $value -PathType Leaf)) {
        throw "Không tìm thấy công cụ dựng video: $value"
    }
}
$root = [IO.Path]::GetFullPath($env:VideoRendering__StorageRoot)
New-Item -ItemType Directory -Force -Path $root,(Join-Path $root 'logs') | Out-Null
if (-not (Get-Module -ListAvailable -Name SqlServer)) { throw 'Thiếu module SqlServer.' }
Import-Module SqlServer
$tables = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database CreatorFlow_QC -Query @"
SELECT
 CASE WHEN OBJECT_ID(N'dbo.VideoAssets',N'U') IS NOT NULL THEN 1 ELSE 0 END AS AssetsReady,
 CASE WHEN OBJECT_ID(N'dbo.VideoRenderJobs',N'U') IS NOT NULL THEN 1 ELSE 0 END AS JobsReady,
 CASE WHEN EXISTS(SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId=N'017_video_render_pipeline') THEN 1 ELSE 0 END AS MigrationReady;
"@ -TrustServerCertificate -ErrorAction Stop)
if ($tables.Count -ne 1 -or $tables[0].AssetsReady -ne 1 -or $tables[0].JobsReady -ne 1 -or $tables[0].MigrationReady -ne 1) {
    throw 'Chưa có migration 017. Chạy Apply-Migrations.ps1 và Verify-Database.ps1 trước.'
}
$stdout = Join-Path $root 'logs\worker-out.log'
$stderr = Join-Path $root 'logs\worker-error.log'
$existing = Get-CimInstance Win32_Process -Filter "Name='CreatorFlow.Workers.exe' OR Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like '*CreatorFlow.Workers*' }
if ($existing) {
    Write-Host 'Dịch vụ dựng video đã chạy; không khởi động thêm bản thứ hai.'
    exit 0
}
$process = Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project','src/CreatorFlow.Workers/CreatorFlow.Workers.csproj','--configuration','Release') -WorkingDirectory $RepositoryPath -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
Start-Sleep -Seconds 4
$process.Refresh()
if ($process.HasExited) {
    Get-Content -LiteralPath $stderr -Tail 40 -ErrorAction SilentlyContinue
    throw 'Worker đã thoát ngay sau khi khởi động. Xem log trong thư mục logs.'
}
Write-Host "Worker dựng video đã khởi động (PID $($process.Id))."
Write-Host "Thư mục video: $root"
Write-Host "Log: $stdout"
