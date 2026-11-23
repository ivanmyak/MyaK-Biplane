# Деплой сервера на Fly.io с той же версией, что получает клиент на Pages.
# Версия считается scripts/get-version.ps1 и пишется в секрет App__Version —
# сервер отдаёт её в GET / и в логах. Запускать из любой папки.

$ErrorActionPreference = "Stop"

$app = "myak-biplane-server"
$fly = "$env:USERPROFILE\.fly\bin\flyctl.exe"

if (-not (Test-Path $fly)) {
    $fly = "flyctl"
}

$version = (& (Join-Path $PSScriptRoot "get-version.ps1")).Trim()
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

Write-Host "Deploying $app, version $version"

Push-Location $repo

try {
    # --stage: не перезапускаем машину дважды (секрет применится при deploy).
    & $fly secrets set "App__Version=$version" --app $app --stage
    if ($LASTEXITCODE -ne 0) { throw "Не удалось установить секрет App__Version" }

    # fly.toml берётся из корня репозитория; --ha=false держит одну машину.
    & $fly deploy --app $app --ha=false --yes
    if ($LASTEXITCODE -ne 0) { throw "Деплой не удался" }

    & $fly status --app $app
}
finally {
    Pop-Location
}
