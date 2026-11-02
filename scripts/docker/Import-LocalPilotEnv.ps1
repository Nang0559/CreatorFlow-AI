function Import-LocalPilotEnv {
    [CmdletBinding()]
    param(
        [string]$Path = (Join-Path (Get-Location) ".env")
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Không tìm thấy file .env tại: $Path. Hãy sao chép .env.example thành .env và điền cấu hình local."
    }

    foreach ($rawLine in Get-Content -LiteralPath $Path) {
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
            continue
        }

        $separator = $line.IndexOf("=")
        if ($separator -le 0) {
            continue
        }

        $name = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).Trim()

        if ($value.Length -ge 2) {
            $first = $value[0]
            $last = $value[$value.Length - 1]
            if (($first -eq '"' -and $last -eq '"') -or ($first -eq "'" -and $last -eq "'")) {
                $value = $value.Substring(1, $value.Length - 2)
            }
        }

        [Environment]::SetEnvironmentVariable($name, $value, "Process")
    }

    $required = @("Authentication__ApiKey", "CreatorFlowApi__ApiKey", "CREATORFLOW_AI_API_KEY", "ConnectionStrings__CreatorFlow")
    foreach ($name in $required) {
        $value = [Environment]::GetEnvironmentVariable($name, "Process")
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "Thiếu giá trị bắt buộc trong .env: $name"
        }
    }

    if ($env:Authentication__ApiKey -ne $env:CreatorFlowApi__ApiKey) {
        throw "CreatorFlowApi__ApiKey phải trùng Authentication__ApiKey cho cấu hình local bootstrap."
    }
    if ($env:Authentication__ApiKey.Length -lt 32 -or $env:CREATORFLOW_AI_API_KEY.Length -lt 32) {
        throw "Authentication__ApiKey và CREATORFLOW_AI_API_KEY phải có ít nhất 32 ký tự."
    }

    Write-Host "Đã nạp cấu hình .env vào tiến trình PowerShell. Không hiển thị giá trị bí mật."
}
