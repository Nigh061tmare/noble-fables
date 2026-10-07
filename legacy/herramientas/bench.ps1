param([string]$Modelo)

$url = "http://127.0.0.1:11436/v1/chat/completions"
$sys = 'Eres el narrador de un reino medieval de los siglos XIII al XV. Escribes UNA replica breve que un noble pronuncia al descubrir que alguien le cae bien o mal. Responde en espanol, UNA o DOS frases, lenguaje de epoca (''don'', ''dona'', ''senor'', ''vosotros''), sin markdown, sin insultos familiares, sin palabras modernas. Si el cambio es positivo, el noble se alegra; si es negativo, se enfada o se cierra en banda. NO controles al otro personaje. Responde SOLO el JSON {"texto":"...","piensa":"..."}.'
$usr = 'Cambio de opinion: Don Edelin empieza a DESCONFIAR Y ODIAR a Nala. Motivo: Nala no tiene lealtad. Su opinion sobre el otro era 42.5 y tras esto queda en 40.2.'

function Llama($nombre, $keep) {
  $body = @{
    model = $nombre; messages = @(
      @{role="system";content=$sys}, @{role="user";content=$usr})
    stream = $false; temperature = 0.85; max_tokens = 150
    reasoning_effort = "none"; think = $false; keep_alive = $keep
  } | ConvertTo-Json -Depth 6 -Compress
  $sw = [Diagnostics.Stopwatch]::StartNew()
  try {
    $r = Invoke-WebRequest $url -Method POST -Body $body -ContentType "application/json; charset=utf-8" -UseBasicParsing -TimeoutSec 180
    $sw.Stop()
    # PS 5.1 decodifica sin charset como ISO-8859-1: hay que releer en UTF-8
    $ms = New-Object IO.MemoryStream
    $r.RawContentStream.CopyTo($ms)
    $txt = [Text.Encoding]::UTF8.GetString($ms.ToArray())
    $j = $txt | ConvertFrom-Json
    "{0,-26} {1,7:N1}s  tok/s={2,-6} {3}" -f $nombre, $sw.Elapsed.TotalSeconds,
      [math]::Round($j.usage.completion_tokens / [math]::Max($sw.Elapsed.TotalSeconds,0.001)),
      ($j.choices[0].message.content -replace '\s+',' ')
  } catch { "{0,-26} FALLO {1}" -f $nombre, $_.Exception.Message }
}

# descarga completa antes de medir, para no contaminar con VRAM de otro modelo
try { & ollama stop $Modelo 2>&1 | Out-Null } catch {}
Start-Sleep 2

Llama $Modelo "10m"   # carga en frio
Llama $Modelo "10m"   # ya caliente
