[CmdletBinding()]
param(
    [string]$AfterEffectsExe = $env:VideoRendering__AfterEffectsExe,
    [string]$AerenderExe = $env:VideoRendering__AerenderExe,
    [string]$FFmpegExe = $env:VideoRendering__FFmpegExe,
    [string]$FFprobeExe = $env:VideoRendering__FFprobeExe
)
$ErrorActionPreference = 'Stop'
$checks = @(
    @{ Name='After Effects'; Path=$AfterEffectsExe },
    @{ Name='aerender.exe'; Path=$AerenderExe },
    @{ Name='ffmpeg.exe'; Path=$FFmpegExe },
    @{ Name='ffprobe.exe'; Path=$FFprobeExe }
)
$failed = $false
foreach ($check in $checks) {
    if ([string]::IsNullOrWhiteSpace($check.Path) -or -not (Test-Path -LiteralPath $check.Path -PathType Leaf)) {
        Write-Host "[THIẾU] $($check.Name): $($check.Path)" -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host "[OK] $($check.Name): $($check.Path)"
    }
}
if ($failed) { throw 'Thiếu thành phần dựng video. Kiểm tra cấu hình trong .env.' }
& $FFmpegExe -version | Select-Object -First 1
& $FFprobeExe -version | Select-Object -First 1
Write-Host 'Đã kiểm tra đường dẫn công cụ. Chưa thực hiện dựng thử video.'
