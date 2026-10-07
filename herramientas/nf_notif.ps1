$G = "D:\SteamLibrary\steamapps\common\Noble Fates"
Add-Type -Path "$G\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Noble Fates_Data\Managed\Assembly-CSharp.dll")
"### tipos con Notification"
$asm.MainModule.Types | Where-Object { $_.Name -match "Notification" } | ForEach-Object {
  " - $($_.Name)"
  $_.Methods | Where-Object { $_.IsPublic -and $_.Name -match "Show|Push|Add|Send|Create" } | Select-Object -First 6 | ForEach-Object {
    "     $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ', '))" }
}
"### PawnManager: opinion/show helpers"
$pm = $asm.MainModule.Types | Where-Object { $_.Name -eq "PawnManager" }
$pm.Methods | Where-Object { $_.Name -match "(?i)notification|bubble|speech|opinion" } | Select-Object -First 20 | ForEach-Object {
  "   $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ', '))" }
"### Singleton/Instance estaticos utiles"
foreach ($n in "PawnManager","UIManager","GameManager") {
  $t = $asm.MainModule.Types | Where-Object { $_.Name -eq $n }
  if ($t) { $t.Fields | Where-Object { $_.IsStatic } | Select-Object -First 6 | ForEach-Object { "   $n.$($_.Name) : $($_.FieldType.Name)" } }
}
