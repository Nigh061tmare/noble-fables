$G = "D:\SteamLibrary\steamapps\common\Noble Fates"
Add-Type -Path "D:\SteamLibrary\steamapps\common\Lords & Villeins\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Noble Fates_Data\Managed\Assembly-CSharp.dll")
$T = $asm.MainModule.Types
"### tipos con Bubble|Speech|Chat|Rumor|Secret|Scheme|Gossip|Dialogue|Conversation (nombre)"
($T | Where-Object { $_.Name -match "Bubble|Speech|Rumor|Secret|Scheme|Gossip|Dialogue|^Conversation|Want$|Wants|Moment$|FloatingText|Emote" -and $_.Name -notmatch "^<" } | Select -First 45 | ForEach-Object { $_.Name }) -join ", "
foreach ($n in "Actor","Pawn") {
  $t = $T | Where-Object { $_.Name -eq $n } | Select -First 1
  "### $n metodos publicos utiles"
  ($t.Methods | Where-Object { $_.IsPublic -and $_.Name -match "Position|Transform|Say|Speak|Bubble|Talk|Emote|Float|Scheme|Want|Moment|Secret|Conversation|Rumor|Gossip|Tell" } | ForEach-Object { "$($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ','))" } | Select -First 30) -join "`n"
}
"### Conversation* tipos y metodos"
foreach ($t in ($T | Where-Object { $_.Name -match "^Conversation|ConversationManager|ConversationSpeaker" } | Select -First 4)) {
  "-- $($t.Name)"; ($t.Methods | Where-Object { $_.IsPublic } | Select -First 18 | ForEach-Object { "   $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ','))" }) -join "`n"
}
"### Scheme* tipos"
foreach ($t in ($T | Where-Object { $_.Name -match "^Scheme" } | Select -First 5)) {
  "-- $($t.Name)"; ($t.Methods | Where-Object { $_.IsPublic -and $_.Name -notmatch "^(get_|set_|\.)" } | Select -First 12 | ForEach-Object { "   $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ','))" }) -join "`n"
}
"### UI: TriggerSpeech|ShowText|Popup en managers"
foreach ($t in ($T | Where-Object { $_.Name -match "UIManager|NotificationManager|PopupManager|CameraManager" } | Select -First 4)) {
  "-- $($t.Name)"; ($t.Methods | Where-Object { $_.IsPublic -and $_.Name -match "Speech|Bubble|Text|Popup|Message|Focus|Follow" } | Select -First 14 | ForEach-Object { "   $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ','))" }) -join "`n"
}
