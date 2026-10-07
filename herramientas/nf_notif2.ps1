$G = "D:\SteamLibrary\steamapps\common\Noble Fates"
Add-Type -Path "$G\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$G\Noble Fates_Data\Managed\Assembly-CSharp.dll")
foreach ($n in "Notification","NotificationManager","UIConversationEsteemChangeNotification") {
  $t = $asm.MainModule.Types | Where-Object { $_.Name -eq $n }
  "### $n hereda: $($t.BaseType.FullName)"
  "  campos:"
  $t.Fields | ForEach-Object { "    {0} {1} {2}" -f $_.FieldType.Name,$_.Name,$(if($_.IsStatic){"[static]"}) }
  "  props:"
  $t.Properties | ForEach-Object { "    $($_.PropertyType.Name) $($_.Name)" }
  "  ctors/metodos publicos:"
  $t.Methods | Where-Object { $_.IsPublic } | ForEach-Object {
    "    $($_.ReturnType.Name) $($_.Name)($(($_.Parameters|%{$_.ParameterType.Name}) -join ', '))" }
}
