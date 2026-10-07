$G = "D:\SteamLibrary\steamapps\common\Noble Fates"
Add-Type -Path "$G\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Noble Fates_Data\Managed\Assembly-CSharp.dll")

foreach ($n in "ISubject","ISubjectOrCompound","PawnManager") {
  $t = $asm.MainModule.Types | Where-Object { $_.Name -eq $n }
  if (-not $t) { "### $n NO ENCONTRADO"; continue }
  "### $n  interface=$($t.IsInterface)  hereda: $($t.BaseType.FullName)"
  "    interfaces: " + (($t.Interfaces | ForEach-Object { $_.InterfaceType.Name }) -join ', ')
}
"### metodos de opinion en PawnManager (firma exacta)"
$pm = $asm.MainModule.Types | Where-Object { $_.Name -eq "PawnManager" }
$pm.Methods | Where-Object { $_.Name -match "GetOpinion" } | ForEach-Object {
  "    $($_.ReturnType.FullName) $($_.Name)(" + (($_.Parameters | ForEach-Object { $_.ParameterType.FullName + ' ' + $_.Name }) -join ', ') + ")"
}
"### Pawn implementa ISubject? ---"
$pawn = $asm.MainModule.Types | Where-Object { $_.Name -eq "Pawn" }
"Pawn interfaces: " + (($pawn.Interfaces | ForEach-Object { $_.InterfaceType.Name }) -join ', ')
"Pawn hereda: $($pawn.BaseType.FullName)"
$act = $asm.MainModule.Types | Where-Object { $_.Name -eq "Actor" }
if ($act) { "Actor interfaces: " + (($act.Interfaces | ForEach-Object { $_.InterfaceType.Name }) -join ', ') }
