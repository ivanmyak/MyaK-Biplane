# Вычисляет версию проекта из git-истории и записывает её в appsettings.json.
# Формат: 0.<слияния в master>.<патч>[-dev].
#   МИНОР: растёт только при слиянии в master;
#   ПАТЧ:  при слиянии = коммиты слитой ветки, при прямых коммитах — коммиты
#          после последнего слияния;
#   -dev:  добавляется для сборок не из master.
# В CI эту же логику выполняет .github/workflows/deploy-pages.yml.

$ErrorActionPreference = "Stop"

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $repo

try {
    $merges = (git rev-list --merges --count HEAD).Trim()
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    $suffix = ""

    # У merge-коммита второй родитель — верхушка слитой ветки.
    $parents = (git rev-list --parents -n 1 HEAD).Trim() -split "\s+"

    if ($branch -ne "master") {
        $originMaster = (git rev-parse --verify origin/master 2>$null).Trim()
        $base = if ($originMaster) { "origin/master" } else { "master" }
        $commits = (git rev-list --count "$base..HEAD").Trim()
        $suffix = "-dev"
    }
    elseif ($parents.Count -ge 3) {
        # Слияние в master: patch обнуляется и становится числом коммитов ветки.
        $commits = (git rev-list --count "$($parents[1])..$($parents[2])").Trim()
    }
    else {
        # Прямой коммит в master: считаем все коммиты после последнего слияния,
        # чтобы версия увеличивалась на каждую правку, а не застывала.
        $lastMerge = (git rev-list --merges -n 1 HEAD).Trim()
        if ([string]::IsNullOrWhiteSpace($lastMerge)) {
            $lastMerge = (git rev-list --max-parents=0 HEAD).Trim()
        }
        $commits = (git rev-list --count "$lastMerge..HEAD").Trim()
    }

    $version = "0.$merges.$commits$suffix"
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
