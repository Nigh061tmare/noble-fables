$G = "D:\SteamLibrary\steamapps\common\Lords & Villeins"
Add-Type -Path "$G\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Lords and Villeins_Data\Managed\Assembly-CSharp.dll")
foreach ($n in "ActiveNPCDB","BaseNPCDB","ActiveOrganization","PlayerManager") {
  $t = $asm.MainModule.Types | Where-Object { $_.Name -eq $n }
  "### $n  hereda: $($t.BaseType.Name)"
  foreach ($f in $t.Fields) { if ($n -ne "ActiveOrganization" -or $f.FieldType.Name -match "Organization|WorkGroup") { "   c {0} {1} {2}" -f $f.FieldType.Name,$f.Name,$(if($f.IsStatic){"[static]"}) } }
  if ($n -ne "PlayerManager") { foreach ($m in $t.Methods) { if ($m.IsPublic -and $m.Name -match "Get|All|Count|Workgroup|WorkGroup") { "   m {0} {1}({2})" -f $m.ReturnType.Name,$m.Name,(($m.Parameters|%{$_.ParameterType.Name}) -join ",") } } }
}
