$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
. (Join-Path $root "dev-shell.ps1")

$indexFile = Join-Path $root "server\wwwroot\index.html"
if (-not (Test-Path $indexFile)) {
    & (Join-Path $root "build-app.ps1")
}

Write-Host ""
Write-Host "Aplicació disponible a http://localhost:5292" -ForegroundColor Cyan
Write-Host "Prem Ctrl+C per aturar-la."

dotnet run --project (Join-Path $root "server\PersonalFinances.Api.csproj") --launch-profile http
