$G = "D:\SteamLibrary\steamapps\common\Noble Fates"
Add-Type -Path "$G\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Noble Fates_Data\Managed\Assembly-CSharp.dll")
foreach ($n in "FeelingReason","Character") {
  $t = $asm.MainModule.Types | Where-Object { $_.Name -eq $n }
  "### $n hereda: $($t.BaseType.Name)"
  foreach ($f in $t.Fields) { "   f {0} {1} {2}" -f $f.FieldType.Name,$f.Name,$(if($f.IsStatic){"[static]"}) }
  foreach ($p in $t.Properties) { if ($p.Name -match "(?i)name|label|desc|value") { "   p {0} {1}" -f $p.PropertyType.Name,$p.Name } }
  foreach ($m in $t.Methods) { if ($m.Name -match "(?i)ToString|GetName|GetLabel") { "   m {0} {1}" -f $m.ReturnType.Name,$m.Name } }
}
