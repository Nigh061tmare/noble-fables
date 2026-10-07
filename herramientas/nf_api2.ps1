$G = "D:\SteamLibrary\steamapps\common\Noble Fates"
Add-Type -Path "D:\SteamLibrary\steamapps\common\Lords & Villeins\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Noble Fates_Data\Managed\Assembly-CSharp.dll")
$T = $asm.MainModule.Types
function Mostrar($nombre, $max, $filtro) {
  $t = $T | Where-Object { $_.Name -eq $nombre } | Select -First 1
  if (-not $t) { "-- $nombre NO EXISTE"; return }
  "-- $nombre : $($t.BaseType.Name)"
  $t.Fields | Where-Object { $_.Name -match $filtro } | Select -First 10 | % { "   f $($_.FieldType.Name) $($_.Name)" }
  $t.Methods | Where-Object { $_.IsPublic -and $_.Name -match $filtro -and $_.Name -notmatch "^\.cctor" } | Select -First $max | % { "   m $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ','))" }
}
Mostrar "ConversationManager" 14 "."
Mostrar "ConversationSpokenWords" 10 "."
Mostrar "ConversationTopic" 8 "."
Mostrar "EmoteCommand" 6 "."
Mostrar "TriggerSchemeOctScriptOperation" 6 "."
"-- Actor fields posicion"
($T | ? Name -eq "Actor" | Select -First 1).Fields | ? { $_.FieldType.Name -match "Vector3|Transform|GameObject" -or $_.Name -match "position|transform" } | Select -First 8 | % { "   f $($_.FieldType.Name) $($_.Name)" }
(($T | ? Name -eq "Actor" | Select -First 1).Properties | ? { $_.PropertyType.Name -match "Vector3|Transform|GameObject" } | Select -First 8 | % { "   p $($_.PropertyType.Name) $($_.Name)" })
"-- Pawn: DeltaOpinionOfSubject exacto"
($T | ? Name -eq "Pawn" | Select -First 1).Methods | ? { $_.Name -match "DeltaOpinionOfSubject|AddOpinionModifier|LearnOpinion|Gossip|Rumor|Secret" } | % { "   $($_.IsPublic) $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name+' '+$_.Name}) -join ', '))" }
"-- FeelingReason ctor / FeelingMemoryFlags"
($T | ? Name -eq "FeelingReason" | Select -First 1).Methods | ? { $_.Name -match "ctor|Create|From" } | % { "   $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ','))" }
($T | ? Name -eq "FeelingMemoryFlags" | Select -First 1).Fields | % { "   enum $($_.Name)" }
"-- tipos *Esteem*|Relationship|Kin con algo social"
($T | ? { $_.Name -match "Esteem|Rumor|Gossip|Whisper" -and $_.Name -notmatch "^<" } | Select -First 12 | % Name) -join ", "
