# ============================================================================
#  PARAR la IA local de Kenshi (libera VRAM)
# ============================================================================
$ErrorActionPreference = "SilentlyContinue"

Write-Host ""
Write-Host "Parando IA de Kenshi..." -ForegroundColor Cyan

$prx = Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
       Where-Object { $_.CommandLine -like "*proxy_kenshi*" }
if ($prx) {
    $prx | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Write-Host ("   proxy parado: {0}" -f $prx.Count)
}

$oll = Get-Process ollama,llama-server -ErrorAction SilentlyContinue
if ($oll) {
    $oll | Stop-Process -Force
    Write-Host ("   ollama parado: {0} (VRAM liberada)" -f $oll.Count)
}

Start-Sleep -Seconds 2
$resto = Get-Process llama-server -ErrorAction SilentlyContinue
if ($resto) {
    Write-Host ("AVISO: quedan {0} llama-server" -f $resto.Count) -ForegroundColor Yellow
} else {
    Write-Host "Todo apagado." -ForegroundColor Green
}
Write-Host ""
