# Записывает версию проекта в клиентский appsettings.json (App:Version).
# Само значение считает scripts/get-version.ps1 — та же логика, что в CI.

$ErrorActionPreference = "Stop"

$version = (& (Join-Path $PSScriptRoot "get-version.ps1")).Trim()
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$path = Join-Path $repo "MyaKBiplane.Client/wwwroot/appsettings.json"

$settings = Get-Content $path -Raw | ConvertFrom-Json

if ($settings.PSObject.Properties.Name -contains "App") {
    $settings.App.Version = $version
}
else {
    $settings | Add-Member -NotePropertyName App -NotePropertyValue ([ordered]@{ Version = $version })
}

$json = $settings | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding $false))

Write-Host "Project version: $version"
