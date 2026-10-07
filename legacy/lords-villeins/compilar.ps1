$ErrorActionPreference = "Stop"
$G  = "D:\SteamLibrary\steamapps\common\Lords & Villeins"
$S  = "C:\Users\Jose Luis\pecera-lv\plugin"
$OUT = "C:\Users\Jose Luis\pecera-lv\plugin\PeceraCerebro.dll"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

$refs = @(
  "$G\BepInEx\core\BepInEx.dll",
  "$G\BepInEx\core\0Harmony.dll",
  "$G\Lords and Villeins_Data\Managed\Assembly-CSharp.dll",
  "$G\Lords and Villeins_Data\Managed\UnityEngine.CoreModule.dll",
  "$G\Lords and Villeins_Data\Managed\UnityEngine.dll"
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { Write-Output "FALTA: $r" } }

$arg = @("-target:library","-out:$OUT","-optimize+","-nologo","-nostdlib+")
foreach ($r in $refs) { $arg += "-r:`"$r`"" }
foreach ($d in @("System.dll","System.Core.dll","System.Xml.dll","mscorlib.dll")) {
  $f = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\$d"
  if (Test-Path $f) { $arg += "-r:`"$f`"" }
}
$arg += "$S\PeceraCerebro.cs"
$arg += "$S\Llm.cs"
$arg += "$S\Parches.cs"
$arg += "$S\Mundo.cs"
$arg += "$S\Rey.cs"
$arg += "$S\GanchoDelRey.cs"
$arg += "$S\Constructor.cs"
$arg += "$S\Manos.cs"
$arg += "$S\Burbujas.cs"
$arg += "$S\Banco.cs"
$arg += "$S\Secretos.cs"
$arg += "$S\Provocador.cs"

Write-Output "=== compilando ==="
& $csc @arg 2>&1 | Select-Object -First 30
Write-Output "=== resultado ==="
if (Test-Path $OUT) {
  Write-Output ("   OK  " + [math]::Round((Get-Item $OUT).Length/1KB,1) + " KB")
} else {
  Write-Output "   NO se genero el DLL"
}