$G = "D:\SteamLibrary\steamapps\common\Noble Fates"
Add-Type -Path "D:\SteamLibrary\steamapps\common\Lords & Villeins\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Noble Fates_Data\Managed\Assembly-CSharp.dll")
$T = $asm.MainModule.Types
function M($n,$filtro,$max){
  $t = $T | ? Name -eq $n | Select -First 1; if(-not $t){"-- $n NO"; return}
  "-- $n : $($t.BaseType.Name)"
  ($t.Fields | ? { $_.Name -match $filtro } | Select -First 8 | % { "   f $($_.FieldType.Name) $($_.Name)" })
  ($t.Methods | ? { $_.IsPublic -and $_.Name -match $filtro -and $_.Name -notmatch "^\.|Tick$" } | Select -First $max | % { "   m $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ','))" })
}
M "PetitionManager" "." 16
M "Petition" "Accept|Approve|Reject|Deny|Resolve|Complete|Decline|petitioner|type|Option" 12
M "QueuedPetition" "." 8
M "ResearchEntry" "id|cost|unlock|Start|Select|Begin|Complete|Prereq|researched|available|Can" 14
M "ResearchCommand" "." 6
M "PlanManager" "Add|Create|Place|Queue|Cancel|Designate|Blueprint|Approve" 14
M "ZoneManager" "Create|Add|Place|Designate|Zone" 10
M "KingdomManager" "Get|Player|Kingdom|Ruler|Vassal" 10
M "SchemeManager" "." 12
M "WantsManager" "." 10
M "MissionManager" "Start|Begin|Send|Create|Mission" 10
