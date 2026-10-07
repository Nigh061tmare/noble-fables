$G = "D:\SteamLibrary\steamapps\common\Noble Fates"
Add-Type -Path "$G\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Noble Fates_Data\Managed\Assembly-CSharp.dll")

$t = $asm.MainModule.Types | Where-Object { $_.Name -eq "Pawn" }
"### Pawn hereda: $($t.BaseType.Name)"
"  -- props con name/character --"
foreach ($p in $t.Properties) { if ($p.Name -match "(?i)name|charact|pawn") { "   p {0} {1}" -f $p.PropertyType.Name,$p.Name } }
"  -- fields con name/charact --"
foreach ($f in $t.Fields) { if ($f.Name -match "(?i)name|charact") { "   f {0} {1}" -f $f.FieldType.Name,$f.Name } }
"  -- metodos con name/charact --"
foreach ($m in $t.Methods) { if ($m.Name -match "(?i)name|charact") { "   m {0} {1}({2})" -f $m.ReturnType.Name,$m.Name,(($m.Parameters|%{$_.ParameterType.Name}) -join ",") } }
"  -- ToString override? --"
foreach ($m in $t.Methods) { if ($m.Name -eq "ToString") { "   ToString en $($m.DeclaringType.Name)" } }

"### PawnManager.OpinionDelta IL"
$pm = $asm.MainModule.Types | Where-Object { $_.Name -eq "PawnManager" }
$md = $pm.Methods | Where-Object { $_.Name -eq "OpinionDelta" }
"  firma: ($($md.Parameters|%{ $_.ParameterType.Name }) -join ', ')"
$md.Body.Instructions | Select-Object -First 45 | ForEach-Object { "   $($_.Offset.ToString('x4'))  $($_.OpCode.Name)  $($_.Operand)" }
