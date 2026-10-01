# Uses a disposable database on Compose MySQL. Tests make no external AI calls.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$settings = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $projectRoot '.env')) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$matches[1]] = $matches[2] }
}
foreach ($required in @('MYSQL_USER', 'MYSQL_PASSWORD', 'MYSQL_PORT')) {
    if (!$settings[$required]) { throw "Missing setting: $required" }
}
if ($settings.MYSQL_USER -notmatch '^[A-Za-z0-9_]+$') { throw 'Test helper requires an alphanumeric/underscore MySQL username.' }
$taskDatabase = 'market_signal_test_' + [Guid]::NewGuid().ToString('N')
if ($taskDatabase -notmatch '^market_signal_test_[a-f0-9]{32}$') { throw 'Invalid generated test database name.' }
$taskPreviousConnection = $env:MARKET_SIGNAL_TEST_MYSQL
$taskCreated = $false
$taskExit = 1
Push-Location $projectRoot
try {
    $taskCreate = 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -uroot -e "CREATE DATABASE __DATABASE__ CHARACTER SET utf8mb4; GRANT ALL PRIVILEGES ON __DATABASE__.* TO ''__USER__''@''%'';"'.Replace('__DATABASE__', $taskDatabase).Replace('__USER__', $settings.MYSQL_USER)
    $taskCreate | docker compose exec -T mysql sh
    if ($LASTEXITCODE -ne 0) { throw 'Could not create isolated MySQL test database.' }
    $taskCreated = $true
    $taskUser = $settings.MYSQL_USER.Replace('"', '""')
    $taskPassword = $settings.MYSQL_PASSWORD.Replace('"', '""')
    $env:MARKET_SIGNAL_TEST_MYSQL = 'Server=127.0.0.1;Port=' + [int]$settings.MYSQL_PORT + ';Database=' + $taskDatabase + ';User ID="' + $taskUser + '";Password="' + $taskPassword + '";DateTimeKind=Utc'
    dotnet test MarketSignalAI.sln --logger 'console;verbosity=minimal'
    $taskExit = $LASTEXITCODE
} finally {
    $env:MARKET_SIGNAL_TEST_MYSQL = $taskPreviousConnection
    if ($taskCreated) {
        $taskDrop = 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -uroot -e "REVOKE ALL PRIVILEGES ON __DATABASE__.* FROM ''__USER__''@''%''; DROP DATABASE __DATABASE__;"'.Replace('__DATABASE__', $taskDatabase).Replace('__USER__', $settings.MYSQL_USER)
        $taskDrop | docker compose exec -T mysql sh
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove disposable database $taskDatabase" }
    }
    Pop-Location
}
exit $taskExit