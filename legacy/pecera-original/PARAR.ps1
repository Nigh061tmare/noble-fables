# ============================================================================
#  PARAR LA PECERA  -  apaga todo limpio
# ============================================================================
#  Se apoya en las LINEAS DE COMANDO y en los PUERTOS, no en nombres de
#  proceso: asi no quedan hijos huerfanos de npm/vite/esbuild, que era el
#  origen de los 78 procesos node zombis.

Write-Host ""
Write-Host "Parando la pecera..." -ForegroundColor Cyan

# --- 1. habitantes -----------------------------------------------------
$hab = Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
       Where-Object { $_.CommandLine -like "*habitante.js*" }
if ($hab) {
    $hab | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Write-Host ("   habitantes parados: {0}" -f $hab.Count)
}

# --- 2. el mundo (npm / vite / esbuild / miniverse) --------------------
$mundo = Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
         Where-Object { $_.CommandLine -match "vite|miniverse|concurrently|esbuild" }
if ($mundo) {
    $mundo | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Write-Host ("   mundo parado: {0} procesos" -f $mundo.Count)
}

# --- 3. quien tenga cogido un puerto (red de seguridad) ----------------
foreach ($pt in @(4321, 5173, 11440)) {
    $dueno = (Get-NetTCPConnection -LocalPort $pt -State Listen -ErrorAction SilentlyContinue).OwningProcess
    foreach ($duenio in ($dueno | Select-Object -Unique)) {
        if ($duenio) {
            Stop-Process -Id $duenio -Force -ErrorAction SilentlyContinue
            Write-Host ("   puerto {0} liberado (pid {1})" -f $pt, $duenio)
        }
    }
}

# --- 4. Ollama (opcional: libera la VRAM) ------------------------------
$oll = Get-Process ollama,llama-server -ErrorAction SilentlyContinue
if ($oll) {
    $oll | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Host ("   ollama parado: {0} procesos (VRAM liberada)" -f $oll.Count)
}

Start-Sleep -Seconds 2

# --- 5. recuento final -------------------------------------------------
$resto = Get-Process node -ErrorAction SilentlyContinue
Write-Host ""
if ($resto) {
    Write-Host ("AVISO: quedan {0} procesos node. Recuento de RAM: {1:N0} MB" -f `
        $resto.Count, (($resto | Measure-Object WorkingSet64 -Sum).Sum/1MB)) -ForegroundColor Yellow
    Write-Host "   para barrerlos:  taskkill /F /IM node.exe" -ForegroundColor Gray
} else {
    Write-Host "Todo apagado, sin procesos node huerfanos." -ForegroundColor Green
}
Write-Host ""
