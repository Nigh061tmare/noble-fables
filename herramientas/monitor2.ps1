param([int]$Minutos = 5)

$G   = "D:\SteamLibrary\steamapps\common\Noble Fates"
$log = "$G\BepInEx\LogOutput.log"
$op  = "$G\BepInEx\plugins\pecera_datos\opiniones.jsonl"
$raw = "$G\BepInEx\plugins\pecera_datos\raw_llm.log"
$fin = (Get-Date).AddMinutes($Minutos)

$visto = 0; if (Test-Path $op)  { $visto = @(Get-Content $op -EA SilentlyContinue).Count }
$vllm  = 0; if (Test-Path $raw) { $vllm  = @(Get-Content $raw -EA SilentlyContinue).Count }

"inicio $(Get-Date -Format HH:mm:ss)  eventos=$visto  llm=$vllm"
"------------------------------------------------------------"

while ((Get-Date) -lt $fin) {
  Start-Sleep 20
  $ev = @(Get-Content $op  -EA SilentlyContinue)
  $ll = @(Get-Content $raw -EA SilentlyContinue)
  $de = $ev.Count - $visto
  $dl = $ll.Count - $vllm
  $visto = $ev.Count; $vllm = $ll.Count

  $gpu = & nvidia-smi --query-gpu=memory.free,utilization.gpu --format=csv,noheader,nounits 2>$null
  $conFrase = @($ev | Where-Object { $_ -match '"dice":\s*"[^"e]' }).Count

  "[{0}] eventos+{1}  llm+{2}  gpu={3}  frases={4}" -f `
      (Get-Date -Format HH:mm:ss), $de, $dl, $gpu, $conFrase

  if ($dl -gt 0) {
    "   ultima respuesta cruda:"
    ($ll | Select-Object -Last 1).Substring(0, [Math]::Min(300, ($ll | Select-Object -Last 1).Length))
  }
}
"fin $(Get-Date -Format HH:mm:ss)"
