param([int]$Minutos = 3)

$G    = "D:\SteamLibrary\steamapps\common\Noble Fates"
$log  = "$G\BepInEx\LogOutput.log"
$op   = "$G\BepInEx\plugins\pecera_datos\opiniones.jsonl"
$fin  = (Get-Date).AddMinutes($Minutos)
$visto = 0
if (Test-Path $op) { $visto = @(Get-Content $op -EA SilentlyContinue).Count }

"monitor activo $(Get-Date -Format HH:mm:ss)  |  linea base: $visto"
"------------------------------------------------------------"

while ((Get-Date) -lt $fin) {
  Start-Sleep 15

  $game = if (Get-Process "Noble Fates" -EA SilentlyContinue) { "VIVO" } else { "CAIDO" }

  $n = @(Get-Content $log -EA SilentlyContinue | Select-String "OpinionDelta llamadas")
  $llamadas = if ($n) { ($n[-1].Line -replace '.*llamadas=','') } else { "-" }

  $lineas = @(Get-Content $op -EA SilentlyContinue)
  $nuevas = $lineas.Count - $visto

  $gpu = (& nvidia-smi --query-gpu=memory.free --format=csv,noheader,nounits 2>$null)
  $ps  = (& ollama ps 2>$null | Select-Object -Skip 1 | Select-Object -First 1)
  $modelo = if ($ps) { ($ps -split '\s{2,}')[0].Trim() + " " + ($ps -split '\s{2,}')[1] } else { "sin modelo cargado" }

  "[{0}] juego={1} llamadas={2} nuevos={3} vram_libre={4}MB modelo={5}" -f `
      (Get-Date -Format HH:mm:ss), $game, $llamadas, $nuevas, $gpu, $modelo

  if ($nuevas -gt 0) {
    $visto = $lineas.Count
    "------------------------------------------------------------"
    "  ULTIMOS EVENTOS:"
    $lineas | Select-Object -Last 4 | ForEach-Object { "  " + $_ }
    "------------------------------------------------------------"
  }
}
"monitor terminado $(Get-Date -Format HH:mm:ss)"
