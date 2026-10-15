$ErrorActionPreference = 'Stop'

$projectFile = Join-Path $PSScriptRoot 'terminal.csproj'
$releaseDirectory = Join-Path $PSScriptRoot 'relese'

$runtimes = @(
    @{ Id = 'win-x64'; Folder = 'win-x64' },
    @{ Id = 'win-arm64'; Folder = 'win-arm64' },
    @{ Id = 'linux-x64'; Folder = 'linux-x64' },
    @{ Id = 'linux-arm64'; Folder = 'linux-arm64' },
    @{ Id = 'osx-x64'; Folder = 'osx-x64' },
    @{ Id = 'osx-arm64'; Folder = 'osx-arm64' }
)

foreach ($runtime in $runtimes) {
    $outputDirectory = Join-Path $releaseDirectory $runtime.Folder

    if (Test-Path -LiteralPath $outputDirectory) {
        Remove-Item -LiteralPath $outputDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

    Write-Host "Publishing Ducky without private commands for $($runtime.Id) to $outputDirectory"

    $appArguments = @(
        'publish', $projectFile,
        '--configuration', 'Release',
        '--runtime', $runtime.Id,
        '--self-contained', 'true',
        '--output', $outputDirectory
    )
    & dotnet @appArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Ducky publish failed for $($runtime.Id)."
    }

    $commandsDirectory = Join-Path $outputDirectory 'Config\Commands'
    $commandProjects = @(
        (Join-Path $PSScriptRoot 'Config\Commands\Hello\hello.csproj'),
        (Join-Path $PSScriptRoot 'Config\Commands\Password\password.csproj')
    )
    foreach ($commandProject in $commandProjects) {
        $commandArguments = @(
            'publish', $commandProject,
            '--configuration', 'Release',
            '--runtime', $runtime.Id,
            '--self-contained', 'true',
            '--output', $commandsDirectory
        )
        & dotnet @commandArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Custom command publish failed for $($runtime.Id): $commandProject"
        }
    }

    if ($runtime.Id -like 'linux-*' -or $runtime.Id -like 'osx-*') {
        $launcherLines = @(
            '#!/usr/bin/env sh',
            'set -eu',
            'APP_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"',
            'cd "$APP_DIR"',
            'chmod +x Ducky Config/Commands/hello Config/Commands/password',
            'exec "$APP_DIR/Ducky" "$@"'
        )
        Set-Content -LiteralPath (Join-Path $outputDirectory 'Ducky.sh') -Value ($launcherLines -join "`n") -NoNewline -Encoding ascii
    }
}

Write-Host "All Ducky variants were published under $releaseDirectory"
