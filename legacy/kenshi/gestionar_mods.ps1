# ============================================================================
#  GESTOR DE MODS DE KENSHI
# ============================================================================
#  Uso:
#    .\gestionar_mods.ps1                     -> lista los ACTIVOS por categoria
#    .\gestionar_mods.ps1 -Buscar nombre      -> busca en activos e inactivos
#    .\gestionar_mods.ps1 -Desactivar "X.mod" -> lo quita de la lista
#    .\gestionar_mods.ps1 -Activar "X.mod"    -> lo anade (al final = prioridad)
#    .\gestionar_mods.ps1 -Inactivos          -> que hay instalado pero apagado
#    .\gestionar_mods.ps1 -Backup             -> copia mods.cfg con fecha
# ============================================================================
param(
    [string]$Buscar,
    [string]$Desactivar,
    [string]$Activar,
    [switch]$Inactivos,
    [switch]$Backup
)

$CFG   = "F:\Steam\steamapps\common\Kenshi\data\mods.cfg"
$MODS  = "F:\Steam\steamapps\common\Kenshi\mods"
$TALLER = @(
    "F:\Steam\steamapps\workshop\content\233860",
    "D:\SteamLibrary\steamapps\workshop\content\233860",
    "H:\SteamLibrary\steamapps\workshop\content\233860",
    "C:\SteamLibrary\steamapps\workshop\content\233860"
)

function Leer-Cfg {
    $t = Get-Content $CFG -Encoding UTF8
    return @($t | Where-Object { $_.Trim() -ne "" })
}
function Escribir-Cfg($lineas) {
    Copy-Item $CFG "$CFG.backup_$(Get-Date -Format yyyyMMdd_HHmmss)" -Force
    # UTF-8 SIN BOM: Kenshi no tolera el BOM que mete Set-Content en PS 5.1
    $utf8sinbom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($CFG, ($lineas -join "`r`n") + "`r`n", $utf8sinbom)
    Write-Host ("   escrito: {0} mods activos" -f $lineas.Count) -ForegroundColor Green
}
function Todos-Disco {
    $enc = @{}
    foreach ($r in @($MODS) + $TALLER) {
        if (Test-Path $r) {
            Get-ChildItem $r -Filter "*.mod" -Recurse -File -ErrorAction SilentlyContinue |
                ForEach-Object { if (-not $enc.ContainsKey($_.Name)) { $enc[$_.Name] = $_.FullName } }
        }
    }
    return $enc
}

# ------------------------------------------------------------------ acciones
if ($Backup) {
    Copy-Item $CFG "$CFG.backup_$(Get-Date -Format yyyyMMdd_HHmmss)" -Force
    Write-Host "Backup hecho." -ForegroundColor Green
    return
}

if ($Desactivar) {
    $l = Leer-Cfg
    if ($l -notcontains $Desactivar) { Write-Host "  no estaba activo: $Desactivar" -ForegroundColor Yellow; return }
    Escribir-Cfg @($l | Where-Object { $_ -ne $Desactivar })
    Write-Host "  DESACTIVADO: $Desactivar" -ForegroundColor Yellow
    return
}

if ($Activar) {
    $l = Leer-Cfg
    if ($l -contains $Activar) { Write-Host "  ya estaba activo: $Activar" -ForegroundColor Yellow; return }
    $disco = Todos-Disco
    if (-not $disco.ContainsKey($Activar)) { Write-Host "  no existe en disco: $Activar" -ForegroundColor Red; return }
    Escribir-Cfg @($l + $Activar)
    Write-Host "  ACTIVADO (al final): $Activar" -ForegroundColor Green
    return
}

if ($Buscar) {
    $act = Leer-Cfg
    $disco = Todos-Disco
    Write-Host ""
    Write-Host "=== resultados para '$Buscar' ===" -ForegroundColor Cyan
    foreach ($k in ($disco.Keys | Sort-Object)) {
        if ($k -like "*$Buscar*") {
            $est = if ($act -contains $k) { "ACTIVO  " } else { "apagado " }
            Write-Host ("   [{0}] {1}" -f $est, $k)
        }
    }
    Write-Host ""
    return
}

$activos = Leer-Cfg
$disco = Todos-Disco

if ($Inactivos) {
    Write-Host ""
    Write-Host "=== INSTALADOS PERO APAGADOS ===" -ForegroundColor Cyan
    $ap = @($disco.Keys | Where-Object { $activos -notcontains $_ } | Sort-Object)
    Write-Host ("   {0} mods" -f $ap.Count) -ForegroundColor Gray
    $ap | ForEach-Object { Write-Host "      $_" }
    Write-Host ""
    return
}

# ------------------------------------------------------------- listado normal
Write-Host ""
Write-Host "==================== MODS ACTIVOS DE KENSHI ====================" -ForegroundColor Cyan
Write-Host ("   {0} activos  |  {1} instalados en disco" -f $activos.Count, $disco.Count) -ForegroundColor Gray
Write-Host ""
$cat = "(sin categoria)"
foreach ($l in $activos) {
    if ($l -match "^\[\d+\]-+\[(.+)\]-+\.mod$") {
        $cat = $Matches[1]
        Write-Host ""
        Write-Host ("--- {0} ---" -f $cat) -ForegroundColor Yellow
    } else {
        $falta = if (-not $disco.ContainsKey($l)) { "   <<< NO ESTA EN DISCO" } else { "" }
        Write-Host ("   {0}{1}" -f $l, $falta)
    }
}
Write-Host ""
Write-Host "  SentientSands debe ser siempre el ULTIMO de la lista." -ForegroundColor Green
Write-Host ""
