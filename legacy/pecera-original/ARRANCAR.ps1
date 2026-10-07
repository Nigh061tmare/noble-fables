# ============================================================================
#  PECERA DE AGENTES - ARRANQUE LIMPIO
# ============================================================================
$ErrorActionPreference = "SilentlyContinue"
$P = "C:\Users\Jose Luis\pecera"
$OLL = "D:\SteamLibrary\steamapps\common\adventureland\ollama\ollama.exe"
$env:OLLAMA_HOST = "127.0.0.1:11434"
$env:OLLAMA_MODELS = "$env:USERPROFILE\.ollama\models"
$env:OLLAMA_ORIGINS = "*"
$env:OLLAMA_KEEP_ALIVE = "30m"

function Esperar($nombre, $url, $segundos) {
    for ($i = 1; $i -le $segundos; $i++) {
        try {
            Invoke-RestMethod $url -TimeoutSec 3 | Out-Null
            Write-Host "   $nombre listo (${i}s)" -ForegroundColor Green
            return $true
        } catch { Start-Sleep -Seconds 1 }
    }
    Write-Host "   $nombre NO respondio tras ${segundos}s" -ForegroundColor Red
    return $false
}

Write-Host ""
Write-Host "=== 1. limpiando restos ===" -ForegroundColor Cyan
Get-Process node -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process llama-server -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process ollama -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Write-Host "   limpio"

Write-Host "=== 2. arrancando Ollama ===" -ForegroundColor Cyan
Start-Process -FilePath $OLL -ArgumentList "serve" -WindowStyle Hidden -ErrorAction SilentlyContinue
if (Esperar "Ollama" "http://127.0.0.1:11434/api/tags" 90) {
    try {
        $tags = Invoke-RestMethod "http://127.0.0.1:11434/api/tags" -TimeoutSec 5
        foreach ($m in $tags.models) { Write-Host "      modelo: $($m.name)" }
    } catch {}
} else {
    Write-Host "   Abortando: sin Ollama los habitantes no pueden pensar." -ForegroundColor Red
    exit 1
}

Write-Host "=== 3. arrancando el mundo ===" -ForegroundColor Cyan
$mundo = Join-Path $P "mundo"
Start-Process -FilePath "cmd" -ArgumentList "/c", "start", "/b", "npm", "run", "dev" -WorkingDirectory $mundo -WindowStyle Hidden -ErrorAction SilentlyContinue
if (-not (Esperar "mundo /api/info" "http://127.0.0.1:4321/api/info" 90)) {
    Write-Host "   Revisa: cd `"$mundo`" ; npm run dev" -ForegroundColor Yellow
}

Write-Host "=== 4. despertando habitantes ===" -ForegroundColor Cyan
$ag = Join-Path $P "agentes"
$logs = Join-Path $P "logs"
New-Item -ItemType Directory -Path $logs -Force | Out-Null

# Quien esta ya despierto? Se identifica por su linea de comandos (fiable).
$ya = @()
Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*habitante.js*" } | ForEach-Object {
        if ($_.CommandLine -match "habitante\.js\s+(\w+)") { $ya += $Matches[1] }
    }

foreach ($n in @("Iris", "Teo", "Nadia")) {
    if ($ya -contains $n) { Write-Host "   $n ya estaba despierto"; continue }
    # SIN redireccion: el propio agente escribe logs\$n.log en UTF-8.
    # (Redirigir con PowerShell re-codificaba la salida y rompia los acentos.)
    Start-Process -FilePath "node" -ArgumentList "habitante.js", $n `
        -WorkingDirectory $ag -WindowStyle Hidden -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
    Write-Host "   $n despierta"
}

Start-Sleep -Seconds 8
Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
try {
    $info = Invoke-RestMethod "http://127.0.0.1:4321/api/info" -TimeoutSec 5
    Write-Host ("  MUNDO: {0}   AGENTES DENTRO: {1}" -f $info.world, $info.agents.total) -ForegroundColor Green
} catch { Write-Host "  (el mundo aun no responde)" -ForegroundColor Yellow }
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  ABRE ESTO EN TU NAVEGADOR:  http://localhost:5173" -ForegroundColor Yellow
Write-Host ""
Write-Host "  logs de los habitantes:  $logs  (Iris.log, Teo.log, Nadia.log)" -ForegroundColor Gray
Write-Host "  decisiones en bruto:     $(Join-Path $P 'datos\eventos.jsonl')" -ForegroundColor Gray
Write-Host "  para parar todo:         PARAR.ps1" -ForegroundColor Gray
Write-Host ""
