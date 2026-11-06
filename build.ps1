<#
.SYNOPSIS
  Publica o Levhrium, gera o instalador e o pacote de atualizacao incremental.

.DESCRIPTION
  Publica self-contained para win-x64 (seus amigos nao precisam instalar o .NET)
  e compila o instalador com o Inno Setup. O instalador cuida sozinho do runtime
  WebView2 se a maquina nao tiver.

  Gera tambem os dois anexos que permitem atualizar sem reinstalar:

    manifest-X.Y.Z.json      hash de cada arquivo da versao
    Levhrium-X.Y.Z-delta.zip so os arquivos que mudaram desde a release anterior

  O navegador compara o manifesto publicado com o que tem no disco e baixa so a
  diferenca -- numa atualizacao normal, os tres arquivos do nosso codigo em vez
  dos 35 MB do instalador.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Version 1.1.0
  .\build.ps1 -SkipInstaller
#>
param(
  [string]$Version,
  [switch]$SkipInstaller,
  [switch]$SkipDelta
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

# --- Manifesto -------------------------------------------------------------
# Caminhos relativos, com barra normal e em minusculas: tem de bater com o que
# UpdateManifest.Normalize produz do outro lado.

Write-Host 'Gerando o manifesto...' -ForegroundColor Cyan

$prefixo = (Resolve-Path $publishDir).Path.TrimEnd('\') + '\'
$arquivos = Get-ChildItem $publishDir -Recurse -File | ForEach-Object {
  [pscustomobject]@{
    path   = $_.FullName.Substring($prefixo.Length).Replace('\', '/').ToLowerInvariant()
    sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    size   = $_.Length
  }
} | Sort-Object path

$manifestPath = Join-Path $distDir "manifest-$Version.json"
[pscustomobject]@{ version = $Version; files = @($arquivos) } |
  ConvertTo-Json -Depth 4 -Compress |
  Set-Content $manifestPath -Encoding utf8

$manifestKb = [math]::Round((Get-Item $manifestPath).Length / 1KB, 1)
Write-Host "Manifesto: $($arquivos.Count) arquivos, $manifestKb KB" -ForegroundColor Green

# --- Pacote incremental ----------------------------------------------------
# Comparado com o manifesto da release anterior. Sem release anterior (ou sem
# o gh por perto) o pacote nao sai, e o navegador cai para o instalador.

if (-not $SkipDelta) {
  $tagAnterior = $null
  try {
    $tagAnterior = (gh release view --json tagName -q .tagName 2>$null)
  } catch {
    $tagAnterior = $null
  }

  if (-not $tagAnterior) {
    Write-Host 'Sem release anterior: nenhum pacote incremental desta vez.' -ForegroundColor Yellow
  }
  else {
    Write-Host "Comparando com $tagAnterior..." -ForegroundColor Cyan

    $tmp = Join-Path $env:TEMP "levhrium-delta-$Version"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null

    $anterior = $null
    try {
      gh release download $tagAnterior -p 'manifest-*.json' -D $tmp 2>$null
      $baixado = Get-ChildItem $tmp -Filter 'manifest-*.json' | Select-Object -First 1
      if ($baixado) { $anterior = Get-Content $baixado.FullName -Raw | ConvertFrom-Json }
    } catch {
      $anterior = $null
    }

    if (-not $anterior) {
      Write-Host "A release $tagAnterior nao tem manifesto: nenhum pacote incremental." -ForegroundColor Yellow
    }
    else {
      $antes = @{}
      foreach ($f in $anterior.files) { $antes[$f.path] = $f.sha256 }

      $mudaram = @($arquivos | Where-Object {
        -not $antes.ContainsKey($_.path) -or $antes[$_.path] -ne $_.sha256
      })

      if ($mudaram.Count -eq 0) {
        Write-Host 'Nenhum arquivo mudou desde a release anterior.' -ForegroundColor Yellow
      }
      else {
        $staging = Join-Path $tmp 'conteudo'
        New-Item -ItemType Directory -Force -Path $staging | Out-Null

        foreach ($f in $mudaram) {
          $destino = Join-Path $staging ($f.path -replace '/', '\')
          New-Item -ItemType Directory -Force -Path (Split-Path $destino -Parent) | Out-Null
          Copy-Item (Join-Path $publishDir ($f.path -replace '/', '\')) $destino
        }

        $deltaPath = Join-Path $distDir "Levhrium-$Version-delta.zip"
        if (Test-Path $deltaPath) { Remove-Item $deltaPath -Force }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $deltaPath)

        $deltaKb = [math]::Round((Get-Item $deltaPath).Length / 1KB, 1)
        $brutoKb = [math]::Round((($mudaram | Measure-Object size -Sum).Sum) / 1KB, 1)

        Write-Host "Pacote incremental: $($mudaram.Count) arquivos, $brutoKb KB -> $deltaKb KB compactados" -ForegroundColor Green
        foreach ($f in $mudaram) { Write-Host "  $($f.path)" -ForegroundColor DarkGray }
      }
    }

    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
  }
}

# --- Instalador ------------------------------------------------------------

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

Write-Host ''
Write-Host 'Para publicar, anexe os tres na release:' -ForegroundColor Cyan
Write-Host "  dist\Levhrium-Setup-$Version.exe" -ForegroundColor DarkGray
Write-Host "  dist\manifest-$Version.json" -ForegroundColor DarkGray
Write-Host "  dist\Levhrium-$Version-delta.zip" -ForegroundColor DarkGray
