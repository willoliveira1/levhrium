<#
.SYNOPSIS
  Publica o Levhrium e gera o instalador.

.DESCRIPTION
  Publica self-contained para win-x64 (seus amigos nao precisam instalar o .NET)
  e compila o instalador com o Inno Setup. O instalador cuida sozinho do runtime
  WebView2 se a maquina nao tiver.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Version 1.1.0
#>
param(
  [string]$Version,
  [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Versao: a passada na linha de comando, senao a do Directory.Build.props.
if (-not $Version) {
  $props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
  if ($props -match '<Version>([^<]+)</Version>') { $Version = $Matches[1] }
  else { throw 'Nao achei <Version> no Directory.Build.props.' }
}

Write-Host "Levhrium $Version" -ForegroundColor Cyan

$publishDir = Join-Path $root 'artifacts\publish'
$distDir = Join-Path $root 'dist'

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir, $distDir | Out-Null

Write-Host 'Publicando (self-contained, win-x64)...' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\Levhrium.App\Levhrium.App.csproj') `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:Version=$Version `
  -p:PublishReadyToRun=true `
  -p:DebugType=none `
  -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'A publicacao falhou.' }

$size = [math]::Round(((Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Publicado: $size MB em $publishDir" -ForegroundColor Green

if ($SkipInstaller) { return }

$iscc = @(
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  throw 'Inno Setup nao encontrado. Instale com: winget install JRSoftware.InnoSetup'
}

Write-Host 'Gerando o instalador...' -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" (Join-Path $root 'installer\Levhrium.iss')
if ($LASTEXITCODE -ne 0) { throw 'A geracao do instalador falhou.' }

$setup = Join-Path $distDir "Levhrium-Setup-$Version.exe"
$setupSize = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "Instalador: $setup ($setupSize MB)" -ForegroundColor Green
