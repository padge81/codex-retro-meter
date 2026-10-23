[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    $compilerPaths = @(
        (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe')
    )
    $compiler = $compilerPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $compiler) { throw 'The .NET Framework C# compiler was not found. Install .NET Framework 4.8.' }
    New-Item -ItemType Directory -Force -Path 'build' | Out-Null
    $references = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll')
    & $compiler /nologo /target:winexe /out:build\RetroUsage.exe /win32manifest:src\app.manifest @references src\RetroUsage.cs
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    Write-Output 'Built build/RetroUsage.exe'

    if ($Test) {
        & $compiler /nologo /target:exe /main:RecoveryTests /out:build\RecoveryTests.exe @references src\RetroUsage.cs tests\RecoveryTests.cs
        if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
        & '.\build\RecoveryTests.exe'
        if ($LASTEXITCODE -ne 0) { throw 'Recovery-policy tests failed.' }
    }

    if ($Package) {
        New-Item -ItemType Directory -Force -Path 'dist' | Out-Null
        Compress-Archive -LiteralPath 'build/RetroUsage.exe','LICENSE','README.md','docs/auto-resume.md','docs/resume-monitor-prompt.md' -DestinationPath 'dist/codex-retro-meter-windows.zip' -Force
        Write-Output 'Packaged dist/codex-retro-meter-windows.zip'
    }
} finally {
    Pop-Location
}
