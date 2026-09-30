$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $projectRoot '.env'
if (!(Test-Path -LiteralPath $settingsPath)) { throw 'Create .env from .env.example first.' }
$settings = @{}
foreach ($line in Get-Content -LiteralPath $settingsPath) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$matches[1]] = $matches[2] }
}
foreach ($required in @('MYSQL_DATABASE', 'MYSQL_USER', 'MYSQL_PASSWORD', 'MYSQL_PORT')) {
    if (!$settings[$required]) { throw "Missing setting: $required" }
}
$taskPreviousProvider = $env:Storage__Provider
$taskPreviousConnection = $env:ConnectionStrings__MySql
try {
    $env:Storage__Provider = 'MySql'
    # Quote values so semicolons cannot add connection options.
    $taskDatabase = $settings.MYSQL_DATABASE.Replace('"', '""')
    $taskUser = $settings.MYSQL_USER.Replace('"', '""')
    $taskPassword = $settings.MYSQL_PASSWORD.Replace('"', '""')
    $env:ConnectionStrings__MySql = 'Server=127.0.0.1;Port=' + [int]$settings.MYSQL_PORT + ';Database="' + $taskDatabase + '";User ID="' + $taskUser + '";Password="' + $taskPassword + '";DateTimeKind=Utc'
    dotnet run --project (Join-Path $projectRoot 'backend/MarketSignalAI.Api')
} finally {
    $env:Storage__Provider = $taskPreviousProvider
    $env:ConnectionStrings__MySql = $taskPreviousConnection
}
