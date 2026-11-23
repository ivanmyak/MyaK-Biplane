# Деплой сервера на Fly.io с той же версией, что получает клиент на Pages.
# Версия считается scripts/get-version.ps1 и пишется в секрет App__Version —
# сервер отдаёт её в GET / и в логах. Запускать из любой папки.
# Если приложение создаётся заново, секреты тоже создаются заново — поэтому
# скрипт ставит и CORS-origin.

param(
    [string]$AllowedOrigin = "https://ivanmyak.github.io"
)

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
    # --stage: не перезапускаем машину дважды (секреты применятся при deploy).
    & $fly secrets set "App__Version=$version" --app $app --stage
    if ($LASTEXITCODE -ne 0) { throw "Failed to set App__Version secret" }

    & $fly secrets set "Cors__AllowedOrigins__0=$AllowedOrigin" --app $app --stage
    if ($LASTEXITCODE -ne 0) { throw "Failed to set CORS secret" }

    # fly.toml берётся из корня репозитория; --ha=false держит одну машину.
    & $fly deploy --app $app --ha=false --yes
    if ($LASTEXITCODE -ne 0) { throw "Fly deploy failed" }

    & $fly status --app $app
}
finally {
    Pop-Location
}
