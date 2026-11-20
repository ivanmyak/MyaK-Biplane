# Вычисляет версию проекта из git-истории и записывает её в appsettings.json.
# Формат: 0.<количество слияний в master>.<коммиты последней слитой ветки>.
# В CI эту же логику выполняет .github/workflows/deploy-pages.yml.

$ErrorActionPreference = "Stop"

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $repo

try {
    $merges = (git rev-list --merges --count HEAD).Trim()

    # У merge-коммита второй родитель — верхушка слитой ветки.
    $parents = (git rev-list --parents -n 1 HEAD).Trim() -split "\s+"
    if ($parents.Count -ge 3) {
        $commits = (git rev-list --count "$($parents[1])..$($parents[2])").Trim()
    } else {
        # Обычный коммит в master: считаем его самого.
        $commits = (git rev-list --count HEAD~1..HEAD).Trim()
    }

    $version = "0.$merges.$commits"
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
}
finally {
    Pop-Location
}
