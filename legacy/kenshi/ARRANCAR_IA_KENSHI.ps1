# ============================================================================
#  ARRANCA LA IA LOCAL PARA KENSHI  (Ollama + proxy + calentado del modelo)
# ============================================================================
#  Orden:  1) Ollama  2) proxy en 11436  3) calienta qwen3.5:9b
#  Despues abre Kenshi TU (con RE_Kenshi.exe) y listo.
# ============================================================================
$ErrorActionPreference = "SilentlyContinue"

$BASE   = "C:\Users\Jose Luis\kenshi-ia"
$OLLAMA = "C:\Users\Jose Luis\agentes-backup\ollama\ollama.exe"
$MODELO = "qwen3.5:9b"

function Esperar($nombre, $url, $segundos) {
    for ($i = 0; $i -lt $segundos; $i++) {
        try { Invoke-RestMethod $url -TimeoutSec 3 | Out-Null; Write-Host ("   {0} responde" -f $nombre) -ForegroundColor Green; return $true } catch { Start-Sleep -Seconds 1 }
    }
    Write-Host ("   {0} NO responde tras {1}s" -f $nombre, $segundos) -ForegroundColor Red
    return $false
}

Write-Host ""
Write-Host "=== 1. limpiando procesos huerfanos ===" -ForegroundColor Cyan
Get-Process llama-server -ErrorAction SilentlyContinue | Stop-Process -Force
$viejo = Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
          Where-Object { $_.CommandLine -like "*proxy_kenshi*" }
if ($viejo) { $viejo | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } }
Start-Sleep -Seconds 2
Write-Host "   limpio"

Write-Host "=== 2. arrancando Ollama ===" -ForegroundColor Cyan
$env:OLLAMA_MODELS    = "$env:USERPROFILE\.ollama\models"
$env:OLLAMA_HOST      = "127.0.0.1:11434"
$env:OLLAMA_ORIGINS   = "*"
$env:OLLAMA_NUM_PARALLEL = "2"
$env:OLLAMA_CONTEXT_LENGTH = "4096"
$env:OLLAMA_FLASH_ATTENTION = "1"
Start-Process -FilePath $OLLAMA -ArgumentList "serve" -WindowStyle Hidden
if (-not (Esperar "Ollama" "http://127.0.0.1:11434/api/tags" 60)) { exit 1 }

Write-Host "=== 3. arrancando el proxy (inyecta reasoning_effort=none) ===" -ForegroundColor Cyan
Start-Process -FilePath "python" -ArgumentList "proxy_kenshi.py" -WorkingDirectory $BASE -WindowStyle Hidden
if (-not (Esperar "proxy" "http://127.0.0.1:11436/v1/models" 40)) { exit 1 }

Write-Host "=== 4. calentando el modelo (para que la primera frase no tarde) ===" -ForegroundColor Cyan
$cuerpo = '{"model":"' + $MODELO + '","messages":[{"role":"user","content":"hola"}],"max_tokens":5}'
try {
    $t0 = Get-Date
    Invoke-RestMethod "http://127.0.0.1:11436/v1/chat/completions" -Method Post `
        -ContentType "application/json" -Body $cuerpo -TimeoutSec 600 | Out-Null
    $s = [math]::Round(((Get-Date) - $t0).TotalSeconds, 1)
    Write-Host ("   {0} cargado en {1} s" -f $MODELO, $s) -ForegroundColor Green
} catch { Write-Host "   el calentado fallo (no es grave)" -ForegroundColor Yellow }

$vram = (nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader)
Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  IA LOCAL LISTA" -ForegroundColor Green
Write-Host ("  VRAM: {0}" -f $vram) -ForegroundColor Gray
Write-Host ("  modelo activo en el mod: {0}" -f $MODELO) -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  AHORA: abre Kenshi con RE_Kenshi.exe" -ForegroundColor Yellow
Write-Host "  En el juego, tecla P = hablar con quien tengas cerca" -ForegroundColor Gray
Write-Host ""
Write-Host "  log del proxy: $BASE\proxy_kenshi.log" -ForegroundColor Gray
Write-Host ""
