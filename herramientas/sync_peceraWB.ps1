# Sincroniza el Core de la pecera con el mod PeceraWB de WorldBox y comprueba que compila (C# 5, como la pecera).
# El adaptador (Mundo/Muestreo/Hooks/VozLlm/Informe) vive en el propio mod: Mods\PeceraWB\Code.
param(
    [string]$Juego = 'F:\Steam\steamapps\common\worldbox'
)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot '..\src\Pecera.Core'
$dst = Join-Path $Juego 'Mods\PeceraWB\Code\Core'
New-Item -ItemType Directory -Path $dst -Force | Out-Null
# No se copian: dependen del mod de Noble Fates o de su sistema de interruptores.
$skip = @('Config.cs', 'Directrices.cs', 'Evidence.cs', 'Compuerta.cs', 'Gobierno.cs', 'Informe.cs', 'Esquemas.cs')
Get-ChildItem $src -Filter *.cs | Where-Object { $skip -notcontains $_.Name } | ForEach-Object { Copy-Item $_.FullName (Join-Path $dst $_.Name) -Force }
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $env:TEMP 'peceraWB_core_check'
New-Item -ItemType Directory -Path $out -Force | Out-Null
& $csc /nologo /target:library /langversion:5 /warnaserror /out:"$out\core.dll" "$dst\*.cs"
if ($LASTEXITCODE -ne 0) { Write-Error 'Core no compila'; exit 1 }
Write-Host 'Core sincronizado y compila.'
