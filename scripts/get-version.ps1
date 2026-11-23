# Единственный расчёт версии проекта из git-истории.
# Используется и update-version.ps1 (клиент), и deploy-fly.ps1 (сервер),
# и дублируется в .github/workflows/deploy-pages.yml (клиент в CI).

$ErrorActionPreference = "Stop"

Push-Location (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

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
        # Прямые коммиты в master: считаем все коммиты после последнего слияния.
        $lastMerge = (git rev-list --merges -n 1 HEAD).Trim()
        if ([string]::IsNullOrWhiteSpace($lastMerge)) {
            $lastMerge = (git rev-list --max-parents=0 HEAD).Trim()
        }
        $commits = (git rev-list --count "$lastMerge..HEAD").Trim()
    }

    Write-Output "0.$merges.$commits$suffix"
}
finally {
    Pop-Location
}
