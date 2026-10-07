<#
.SYNOPSIS
  Compila el plugin Pecera (src/Pecera.Core + src/Pecera.Game) contra tu Noble Fates y,
  opcionalmente, lo instala.

.PARAMETER Juego
  Carpeta de Noble Fates. Si se omite se busca en $env:NOBLE_FATES_DIR y en las bibliotecas de Steam.

.PARAMETER Instalar
  Copia PeceraNF.dll a BepInEx\plugins. NUNCA cierra el juego: si esta abierto, aborta y te lo dice
  (el DLL esta bloqueado mientras el juego corre y cerrarlo sin que guardes perderia la partida).

.EXAMPLE
  .\compilar.ps1 -Juego "D:\SteamLibrary\steamapps\common\Noble Fates" -Instalar
#>
param(
  [string]$Juego = $env:NOBLE_FATES_DIR,
  [string]$Salida = (Join-Path $PSScriptRoot "dist"),
  [switch]$Instalar
)
$ErrorActionPreference = "Stop"

# ---- 1. localizar el juego ----
function Busca-Juego {
  $cand = @()
  foreach ($raiz in @("C:\Program Files (x86)\Steam", "C:\Program Files\Steam")) { $cand += Join-Path $raiz "steamapps\common\Noble Fates" }
  foreach ($u in "C","D","E","F","G") { $cand += "${u}:\SteamLibrary\steamapps\common\Noble Fates"; $cand += "${u}:\Steam\steamapps\common\Noble Fates" }
  $vdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
  if (Test-Path $vdf) {
    foreach ($l in Get-Content $vdf) { if ($l -match '"path"\s+"([^"]+)"') { $cand += (Join-Path ($Matches[1] -replace '\\\\','\') "steamapps\common\Noble Fates") } }
  }
  foreach ($c in $cand) { if (Test-Path (Join-Path $c "BepInEx\core\BepInEx.dll")) { return $c } }
  return $null
}
if (-not $Juego) { $Juego = Busca-Juego }
if (-not $Juego -or -not (Test-Path $Juego)) {
  Write-Output "No encuentro Noble Fates con BepInEx instalado. Pasa -Juego <carpeta> o define NOBLE_FATES_DIR."
  exit 1
}
$d = (Get-ChildItem $Juego -Directory -Filter "*_Data" | Select-Object -First 1).FullName
if (-not $d) { Write-Output "No hay carpeta *_Data en $Juego"; exit 1 }

# ---- 2. compilador: csc de .NET Framework 4 (C# 5; el Core esta escrito en C# 5 a proposito) ----
$csc = $null
foreach ($c in @("$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe", "$env:windir\Microsoft.NET\Framework\v4.0.30319\csc.exe")) {
  if (Test-Path $c) { $csc = $c; break }
}
if (-not $csc) { Write-Output "No encuentro csc.exe de .NET Framework 4."; exit 1 }
$fw = Split-Path $csc

$refs = @(
  "$Juego\BepInEx\core\BepInEx.dll",
  "$Juego\BepInEx\core\0Harmony.dll",
  "$d\Managed\Assembly-CSharp.dll",
  "$d\Managed\UnityEngine.dll",
  "$d\Managed\UnityEngine.CoreModule.dll",
  "$d\Managed\UnityEngine.IMGUIModule.dll",
  "$d\Managed\UnityEngine.TextRenderingModule.dll",
  "$d\Managed\UnityEngine.InputLegacyModule.dll",
  "$fw\System.dll", "$fw\System.Core.dll", "$fw\mscorlib.dll"
)
$falta = @($refs | Where-Object { -not (Test-Path $_) })
if ($falta.Count -gt 0) { $falta | ForEach-Object { Write-Output "FALTA: $_" }; exit 1 }

# ---- 3. fuentes ----
$fuentes = @(Get-ChildItem (Join-Path $PSScriptRoot "src\Pecera.Core") -Filter *.cs -File) +
           @(Get-ChildItem (Join-Path $PSScriptRoot "src\Pecera.Game") -Filter *.cs -File)
if ($fuentes.Count -eq 0) { Write-Output "No hay fuentes en src\"; exit 1 }

New-Item -ItemType Directory -Force $Salida | Out-Null
$OUT = Join-Path $Salida "PeceraNF.dll"
# Se borra el DLL previo: si la compilacion falla no debe quedar un binario viejo haciendose pasar por bueno.
Remove-Item $OUT -Force -ErrorAction SilentlyContinue

$arg = @("-target:library", "-out:$OUT", "-optimize+", "-nologo", "-warnaserror+", "-codepage:65001")
foreach ($r in $refs) { $arg += "-r:`"$r`"" }
# Las rutas de fuentes van SIN comillas anadidas (PowerShell ya cita lo que lleva espacios), como en la v0.1.
foreach ($f in $fuentes) { $arg += $f.FullName }

Write-Output "=== compilando PeceraNF ($($fuentes.Count) ficheros) ==="
$log = & $csc @arg 2>&1
$log | ForEach-Object { Write-Output $_ }
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $OUT)) {
  Write-Output "=== FALLO (codigo $LASTEXITCODE) ==="
  exit 1
}
Write-Output ("=== OK  {0} KB  -> {1}" -f [math]::Round((Get-Item $OUT).Length / 1KB, 1), $OUT)

# ---- 4. instalar (sin cerrar nunca el juego) ----
if ($Instalar) {
  $proc = Get-Process | Where-Object { $_.Path -and $_.Path -like "$Juego*" }
  if ($proc) {
    Write-Output "El juego esta ABIERTO. No lo cierro por ti (podrias perder la partida). Guarda, cierralo tu y vuelve a ejecutar con -Instalar."
    exit 2
  }
  $dest = Join-Path $Juego "BepInEx\plugins"
  New-Item -ItemType Directory -Force $dest | Out-Null
  Copy-Item $OUT (Join-Path $dest "PeceraNF.dll") -Force
  Write-Output "Instalado en $dest. Datos y config.txt en $dest\pecera_datos tras el primer arranque."
}
exit 0
