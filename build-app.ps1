$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
. (Join-Path $root "dev-shell.ps1")

Push-Location (Join-Path $root "client")
try {
    npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "La compilació d'Angular ha fallat."
    }
}
finally {
    Pop-Location
}

$webRoot = Join-Path $root "server\wwwroot"
$clientOutput = Join-Path $root "client\dist\client\browser"

if (Test-Path $webRoot) {
    Remove-Item -Path $webRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
Copy-Item -Path (Join-Path $clientOutput "*") -Destination $webRoot -Recurse

dotnet build (Join-Path $root "PersonalFinances.slnx") --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "La compilació de .NET ha fallat."
}

Write-Host ""
Write-Host "Aplicació compilada correctament." -ForegroundColor Green
