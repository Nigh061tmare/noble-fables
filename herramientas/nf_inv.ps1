Add-Type -Path "D:\SteamLibrary\steamapps\common\Lords & Villeins\BepInEx\core\Mono.Cecil.dll"
$dll = "D:\SteamLibrary\steamapps\common\Noble Fates\Noble Fates_Data\Managed\Assembly-CSharp.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
foreach ($t in $asm.MainModule.Types) {
  foreach ($m in $t.Methods) {
    if (-not $m.HasBody) { continue }
    foreach ($i in $m.Body.Instructions) {
      $o = $i.Operand
      if ($o -is [Mono.Cecil.MethodReference] -and $o.DeclaringType.Name -eq "PawnOpinionDeltaCallback" -and $o.Name -eq "Invoke") {
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ", "
        "{0}::{1}({2})" -f $t.FullName, $m.Name, $ps
        break
      }
    }
  }
  foreach ($n in $t.NestedTypes) {
    foreach ($m in $n.Methods) {
      if (-not $m.HasBody) { continue }
      foreach ($i in $m.Body.Instructions) {
        $o = $i.Operand
        if ($o -is [Mono.Cecil.MethodReference] -and $o.DeclaringType.Name -eq "PawnOpinionDeltaCallback" -and $o.Name -eq "Invoke") { "{0}/{1}::{2}" -f $t.FullName,$n.Name,$m.Name; break }
      }
    }
  }
}
"--- campos con ese delegado ---"
foreach ($t in $asm.MainModule.Types) { foreach ($f in $t.Fields) { if ($f.FieldType.Name -eq "PawnOpinionDeltaCallback") { "{0}.{1}" -f $t.Name,$f.Name } } }
"--- Opinion.DeltaValue / FeelingMemory publicos ---"
$t = $asm.MainModule.Types | Where-Object { $_.Name -eq "Pawn" }
foreach ($m in $t.Methods) { if ($m.Name -match "Opinion|Feeling") { "{0} {1}({2})" -f $m.ReturnType.Name,$m.Name,(($m.Parameters|%{$_.ParameterType.Name}) -join ",") } }
