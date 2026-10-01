param([switch]$LiveAI)
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
$taskOptionalSettings = @{
    OPENAI_ENABLED = 'OpenAI__Enabled'
    OPENAI_API_KEY = 'OPENAI_API_KEY'
    OPENAI_MODEL = 'OpenAI__Model'
    OPENAI_REASONING_EFFORT = 'OpenAI__ReasoningEffort'
    OPENAI_MAX_OUTPUT_TOKENS = 'OpenAI__MaxOutputTokens'
    MARKET_WORKER_ENABLED = 'MarketWorker__Enabled'
    DEEPSEEK_ENABLED = 'DeepSeek__Enabled'
    DEEPSEEK_MODEL = 'DeepSeek__Model'
    DEEPSEEK_BASE_URL = 'DeepSeek__BaseUrl'
}
$taskPreviousOptionalSettings = @{}
foreach ($name in $taskOptionalSettings.Values) {
    $taskPreviousOptionalSettings[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    foreach ($name in $taskOptionalSettings.Keys) {
        if ($settings.ContainsKey($name)) {
            [Environment]::SetEnvironmentVariable($taskOptionalSettings[$name], $settings[$name], 'Process')
        }
    }
    if ($LiveAI) {
        $env:OpenAI__Enabled = 'true'
        $env:DeepSeek__Enabled = 'true'
        $env:MarketWorker__Enabled = 'false'
    }
    $env:Storage__Provider = 'MySql'
    # Quote values so semicolons cannot add connection options.
    $taskDatabase = $settings.MYSQL_DATABASE.Replace('"', '""')
    $taskUser = $settings.MYSQL_USER.Replace('"', '""')
    $taskPassword = $settings.MYSQL_PASSWORD.Replace('"', '""')
    $env:ConnectionStrings__MySql = 'Server=127.0.0.1;Port=' + [int]$settings.MYSQL_PORT + ';Database="' + $taskDatabase + '";User ID="' + $taskUser + '";Password="' + $taskPassword + '";DateTimeKind=Utc'
    dotnet run --project (Join-Path $projectRoot 'backend/MarketSignalAI.Api')
} finally {
    foreach ($name in $taskOptionalSettings.Values) {
        [Environment]::SetEnvironmentVariable($name, $taskPreviousOptionalSettings[$name], 'Process')
    }
    $env:Storage__Provider = $taskPreviousProvider
    $env:ConnectionStrings__MySql = $taskPreviousConnection
}
