#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$SqlInstance = 'localhost',
    [string]$Database = 'CreatorFlow_QC',
    [switch]$SkipDockerBuild,
    [switch]$SkipDotNetTests,
    [switch]$SkipPythonTests,
    [switch]$SkipSqlInspection,
    [switch]$StartCompose,
    [switch]$AllowDirtyWorkingTree
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$reportDir = Join-Path $repoRoot 'artifacts\\z240-validation'
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$reportPath = Join-Path $reportDir "z240-validation-$stamp.txt"
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result([string]$Name, [string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ Check=$Name; Status=$Status; Detail=$Detail })
    Write-Host ("[{0}] {1} - {2}" -f $Status, $Name, $Detail)
}
function Invoke-Check([string]$Name, [scriptblock]$Action) {
    try {
        $global:LASTEXITCODE = 0
        $output = & $Action 2>&1
        $commandSucceeded = $?
        $nativeExitCode = $global:LASTEXITCODE
        if (-not $commandSucceeded) { throw 'Lệnh hệ thống kết thúc không thành công.' }
        if ($nativeExitCode -ne 0) {
            $detail = ($output | Out-String).Trim()
            if (-not $detail) { $detail = "Native command exit code: $nativeExitCode" }
            throw $detail
        }
        $text = ($output | Out-String).Trim()
        Add-Result $Name 'PASS' ($(if ($text) { $text } else { 'Hoàn tất.' }))
        return $true
    } catch {
        Add-Result $Name 'FAIL' $_.Exception.Message
        return $false
    }
}
function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "Thiếu lệnh '$Name'." }
}

Write-Host "CreatorFlow Z240 validation | $stamp"
Write-Host "Thư mục dự án: $repoRoot"
Write-Host 'Không in nội dung .env, API key, mật khẩu hoặc connection string.'

$gitBranch = (& git branch --show-current 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Không xác định được Git branch.' }
if ($gitBranch -ne 'feature/keycloak-oidc-local') {
    throw "Đang ở nhánh '$gitBranch'. Hãy chuyển sang feature/trend-selection-safety-intelligence rồi chạy lại."
}
$dirty = (& git status --porcelain 2>$null | Out-String).Trim()
if ($dirty -and -not $AllowDirtyWorkingTree) {
    throw 'Working tree đang có thay đổi. Commit/stash hoặc chạy lại với -AllowDirtyWorkingTree sau khi đã lưu an toàn.'
}
Add-Result 'Git branch và working tree' 'PASS' ("Branch=$gitBranch; clean=$([string]::IsNullOrWhiteSpace($dirty))")

$requiredFiles = @('CreatorFlow.sln','compose.dev.yaml','.env','ai')
foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Join-Path $repoRoot $file))) { throw "Thiếu thành phần bắt buộc: $file" }
}
Add-Result 'Tệp cấu hình cục bộ' 'PASS' 'Đã có solution, Compose, .env và thư mục AI.'

Require-Command dotnet
if (-not $SkipDotNetTests) {
    Invoke-Check 'DotNET restore' { dotnet restore CreatorFlow.sln }
    Invoke-Check 'DotNET build' { dotnet build CreatorFlow.sln --no-restore }
    Invoke-Check 'DotNET tests' { dotnet test CreatorFlow.sln --no-build --logger "trx;LogFileName=z240-$stamp.trx" }
}
if (-not $SkipPythonTests) {
    Require-Command python
    & python -c "import importlib.util,sys; sys.exit(0 if importlib.util.find_spec('pytest') else 1)" 2>$null
    $pytestProbeExitCode = $LASTEXITCODE
    if ($pytestProbeExitCode -ne 0) {
        Add-Result 'Python AI tests' 'BLOCKED' "Python=$((python --version 2>&1 | Out-String).Trim()); thiếu pytest nên không chạy test."
    } else {
        Invoke-Check 'Python AI tests' { python -m pytest ai/tests -q }
    }
}
Require-Command docker
Invoke-Check 'Docker daemon' { docker info --format 'Docker daemon sẵn sàng: {{.ServerVersion}}' }
Invoke-Check 'Docker Compose config' { docker compose --env-file .env -f compose.dev.yaml config --quiet }
if (-not $SkipDockerBuild) {
    Invoke-Check 'Docker images build' { docker compose --env-file .env -f compose.dev.yaml build }
}
if ($StartCompose) {
    Invoke-Check 'Start Compose services' { docker compose --env-file .env -f compose.dev.yaml up -d }
}
Invoke-Check 'Compose service status' { docker compose --env-file .env -f compose.dev.yaml ps }

# API is launched natively on Windows in the current Z240 setup; web and AI are in Compose.
foreach ($endpoint in @(
    @{ Name='Web'; Url='http://localhost:8080' },
    @{ Name='AI'; Url='http://localhost:8001/health' },
    @{ Name='API'; Url='http://localhost:7001/health' }
)) {
    try {
        $response = Invoke-WebRequest -Uri $endpoint.Url -Method Get -TimeoutSec 12 -SkipHttpErrorCheck
        if ([int]$response.StatusCode -lt 200 -or [int]$response.StatusCode -ge 400) {
            throw "HTTP $($response.StatusCode)"
        }
        Add-Result ("HTTP " + $endpoint.Name) 'PASS' ("HTTP $($response.StatusCode) từ $($endpoint.Url)")
    } catch {
        Add-Result ("HTTP " + $endpoint.Name) 'FAIL' $_.Exception.Message
    }
}

if (-not $SkipSqlInspection) {
    if (Get-Module -ListAvailable -Name SqlServer) {
        try {
            Import-Module SqlServer -ErrorAction Stop
            $rows = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate -Query @"
SELECT MigrationId, ScriptName, AppliedAt
FROM dbo.SchemaMigrations
ORDER BY MigrationId;
"@ -ErrorAction Stop)
            $latest = if ($rows.Count) { $rows[-1].MigrationId } else { 'Chưa có migration' }
            Add-Result 'SQL migration ledger (chỉ đọc)' 'PASS' ("Đọc được $($rows.Count) migration; mới nhất: $latest")
            $required = @('021_channel_sync_and_video_download_queue', '022_tenant_subscriptions_and_usage', '023_tenant_member_contact')
            foreach ($id in $required) {
                if ($rows.MigrationId -notcontains $id) {
                    Add-Result "SQL migration $id" 'BLOCKED' 'Chưa áp dụng. Script kiểm tra không tự chạy migration.'
                }
            }
        } catch {
            Add-Result 'SQL migration ledger (chỉ đọc)' 'FAIL' $_.Exception.Message
        }
    } else {
        Add-Result 'SQL migration ledger (chỉ đọc)' 'BLOCKED' 'Máy chưa cài module SqlServer; không tự cài và không thay đổi database.'
    }
}

$results | Format-Table -Wrap -AutoSize | Out-String -Width 240 | Set-Content -LiteralPath $reportPath -Encoding utf8
$failed = @($results | Where-Object Status -eq 'FAIL').Count
$blocked = @($results | Where-Object Status -eq 'BLOCKED').Count
$passed = @($results | Where-Object Status -eq 'PASS').Count
$summary = "Kết quả: $passed PASS, $failed FAIL, $blocked BLOCKED. Báo cáo: $reportPath"
Write-Host $summary
if ($failed -gt 0) { exit 1 }
if ($blocked -gt 0) { exit 2 }
