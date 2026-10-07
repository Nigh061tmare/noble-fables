$G = "D:\SteamLibrary\steamapps\common\Noble Fates"
Add-Type -Path "D:\SteamLibrary\steamapps\common\Lords & Villeins\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Noble Fates_Data\Managed\Assembly-CSharp.dll")
$T = $asm.MainModule.Types | Where-Object { $_.Name -notmatch "^<|OctScript|Condition$|Operation$" }
"### tipos de gestion"
($T | Where-Object { $_.Name -match "Research|Tech|Blueprint|Designation|Zone$|Zones|WorkGroup|Labor|Task|Job$|Order$|Edict|Policy|Ambition|Proclamation|Petition|Appointment|Role$|Title" } | Select -First 60 | % Name) -join ", "
"### Manager singletons"
($T | Where-Object { $_.Name -match "Manager$" } | % Name) -join ", "
foreach ($n in "ResearchManager","BlueprintManager","TaskManager","DesignationManager","KingdomManager","AppointmentManager","PlayerManager") {
  $t = $T | ? Name -eq $n | Select -First 1
  if ($t) { "-- $n"; ($t.Methods | ? { $_.IsPublic -and $_.Name -notmatch "^(get_|set_|\.|Tick|Awake|Start|Load|Save)" } | Select -First 18 | % { "   $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ','))" }) -join "`n" }
}
