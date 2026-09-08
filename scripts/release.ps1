param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$DryRun,
    [switch]$SkipBuild,
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$releaseCommitted = $false
Push-Location $root

try {
    function Set-Utf8NoBomContent {
        param([string]$Path, [string]$Content)
        [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
    }

    function Invoke-Git {
        & git @args
        if ($LASTEXITCODE -ne 0) {
            throw "git $($args -join ' ') failed with exit code $LASTEXITCODE."
        }
    }

    function Invoke-Gh {
        & gh @args
        if ($LASTEXITCODE -ne 0) {
            throw "gh $($args -join ' ') failed with exit code $LASTEXITCODE."
        }
    }

    if ((Invoke-Git branch --show-current) -ne 'main') {
        throw 'Releases must be created from main.'
    }

    $trackedChanges = @(Invoke-Git status --short --untracked-files=no)
    if (-not $DryRun -and $trackedChanges.Count -ne 0) {
        throw "Tracked files have uncommitted changes:`n$($trackedChanges -join "`n")"
    }

    git diff --quiet -- Beastmaster.csproj Beastmaster.json repo.json
    if ($LASTEXITCODE -ne 0) {
        throw 'Beastmaster.csproj, Beastmaster.json, or repo.json has uncommitted changes.'
    }

    Invoke-Git fetch origin main --tags
    $divergence = Invoke-Git rev-list --left-right --count origin/main...main
    if ($divergence -ne "0`t0") {
        throw "main differs from origin/main ($divergence). Pull or push before releasing."
    }

    if (Invoke-Git tag --list $Version) {
        throw "Tag $Version already exists locally."
    }

    if (Invoke-Git ls-remote --tags origin "refs/tags/$Version") {
        throw "Tag $Version already exists on origin."
    }

    $timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    $projectPath = Join-Path $root 'Beastmaster.csproj'
    $manifestPath = Join-Path $root 'Beastmaster.json'
    $repoPath = Join-Path $root 'repo.json'

    $project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
    $project = [regex]::Replace($project, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version</AssemblyVersion>", 1)
    Set-Utf8NoBomContent $projectPath $project

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
    $manifest = [regex]::Replace($manifest, '("AssemblyVersion"\s*:\s*")[^"]+("\s*)', "`${1}$Version`${2}", 1)
    Set-Utf8NoBomContent $manifestPath $manifest

    $repo = Get-Content -LiteralPath $repoPath -Raw -Encoding UTF8
    $downloadUrl = "https://github.com/anmili2022/Beastmaster/releases/download/$Version/Beastmaster.zip"
    $repo = [regex]::Replace($repo, '("AssemblyVersion"\s*:\s*")[^"]+("\s*)', "`${1}$Version`${2}", 1)
    foreach ($property in @('DownloadLinkInstall', 'DownloadLinkTesting', 'DownloadLinkUpdate')) {
        $pattern = '("{0}"\s*:\s*")[^"]+("\s*)' -f $property
        $repo = [regex]::Replace($repo, $pattern, "`${1}$downloadUrl`${2}", 1)
    }
    $repo = [regex]::Replace($repo, '("LastUpdated"\s*:\s*")[^"]+("\s*)', "`${1}$timestamp`${2}", 1)
    Set-Utf8NoBomContent $repoPath $repo

    if ($DryRun) {
        git diff -- Beastmaster.csproj Beastmaster.json repo.json
        Invoke-Git restore -- Beastmaster.csproj Beastmaster.json repo.json
        Write-Host "Dry run completed for $Version; version files were restored."
        exit 0
    }

    if (-not $SkipBuild) {
        & dotnet build --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw 'Release build failed.'
        }
    }

    Invoke-Git add -- Beastmaster.csproj Beastmaster.json repo.json
    Invoke-Git commit -m "Release $Version"
    $releaseCommitted = $true
    Invoke-Git push origin main
    Invoke-Git tag -a $Version -m "Release $Version"
    Invoke-Git push origin $Version

    if ($NoWait) {
        Write-Host "Triggered release $Version. GitHub Actions will publish it in the background."
        exit 0
    }

    $runId = $null
    $headSha = Invoke-Git rev-parse HEAD
    for ($attempt = 0; $attempt -lt 20 -and -not $runId; $attempt++) {
        Start-Sleep -Seconds 2
        $runJson = gh run list --workflow 'Create Release' --event push --limit 10 --json databaseId,headSha | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to query the release workflow.'
        }
        $run = $runJson | Where-Object { $_.headSha -eq $headSha } | Select-Object -First 1
        if ($null -ne $run) {
            $runId = $run.databaseId
        }
    }

    if (-not $runId) {
        throw 'Release workflow did not appear within 40 seconds.'
    }

    Invoke-Gh run watch $runId --exit-status

    $release = gh release view $Version --json url,assets | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $release.assets.name -notcontains 'Beastmaster.zip') {
        throw 'Release exists but Beastmaster.zip was not found.'
    }

    Write-Host "Published ${Version}: $($release.url)"
}
catch {
    if (-not $releaseCommitted) {
        git restore -- Beastmaster.csproj Beastmaster.json repo.json 2>$null
    }
    throw
}
finally {
    Pop-Location
}
