$ErrorActionPreference = "Stop"
$G  = "D:\SteamLibrary\steamapps\common\Noble Fates"
$d  = (Get-ChildItem $G -Directory -Filter "*_Data" | Select-Object -First 1).FullName
$S  = "C:\Users\Jose Luis\pecera-nf\plugin"
$OUT = "C:\Users\Jose Luis\pecera-nf\plugin\PeceraNF.dll"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

$refs = @(
  "$G\BepInEx\core\BepInEx.dll",
  "$G\BepInEx\core\0Harmony.dll",
  "$d\Managed\Assembly-CSharp.dll",
  "$d\Managed\UnityEngine.CoreModule.dll",
  "$d\Managed\UnityEngine.IMGUIModule.dll",
  "$d\Managed\UnityEngine.TextRenderingModule.dll",
  "$d\Managed\UnityEngine.InputLegacyModule.dll",
  "$d\Managed\UnityEngine.dll"
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { Write-Output "FALTA: $r" } }

$arg = @("-target:library", "-out:$OUT", "-optimize+", "-nologo")
foreach ($r in $refs) { $arg += "-r:`"$r`"" }
foreach ($f in @("System.dll","System.Core.dll","mscorlib.dll")) {
  $p = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\$f"
  if (Test-Path $p) { $arg += "-r:`"$p`"" }
}
$arg += "$S\Pecera.cs"
$arg += "$S\Llm.cs"
$arg += "$S\Gancho.cs"
$arg += "$S\Pantalla.cs"

Write-Output "=== compilando PeceraNF ==="
# Se borra el DLL previo: si la compilacion falla, no debe quedar un binario
# viejo haciendose pasar por bueno.
Remove-Item $OUT -Force -ErrorAction SilentlyContinue
$log = & $csc @arg 2>&1
$code = $LASTEXITCODE
$log | Select-Object -First 25 | ForEach-Object { Write-Output $_ }
$errores = @($log | Where-Object { $_ -match "error CS" })
if ($errores.Count -gt 0) {
  Write-Output ("=== FALLO: " + $errores.Count + " error(es) ===")
  exit 1
}
Write-Output "=== resultado ==="
if (Test-Path $OUT) {
  Write-Output ("   OK  " + [math]::Round((Get-Item $OUT).Length/1KB,1) + " KB")
} else {
  Write-Output "   NO se genero"
  exit 1
}