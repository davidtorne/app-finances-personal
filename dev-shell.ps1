$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$nodeDir = Join-Path $root ".tools\node-v24.15.0-win-x64"
$npmPrefix = Join-Path $root ".tools\npm-global"
$dotnetDir = Join-Path $root ".tools\dotnet"

$env:PATH = "$npmPrefix;$nodeDir;$dotnetDir;$env:PATH"
$env:DOTNET_ROOT = $dotnetDir

function global:node {
    & (Join-Path $nodeDir "node.exe") @args
}

function global:npm {
    & (Join-Path $nodeDir "node.exe") `
        (Join-Path $npmPrefix "node_modules\npm\bin\npm-cli.js") @args
}

function global:npx {
    & (Join-Path $nodeDir "node.exe") `
        (Join-Path $npmPrefix "node_modules\npm\bin\npx-cli.js") @args
}

function global:ng {
    & (Join-Path $npmPrefix "ng.cmd") @args
}

function global:tsc {
    & (Join-Path $npmPrefix "tsc.cmd") @args
}

function global:dotnet {
    & (Join-Path $dotnetDir "dotnet.exe") @args
}

Write-Host "Entorn de desenvolupament carregat."
Write-Host "Node $(node --version) | npm $(npm --version) | TypeScript $(tsc --version) | .NET $(dotnet --version)"
