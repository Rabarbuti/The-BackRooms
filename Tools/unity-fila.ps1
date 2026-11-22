# Fila do Unity: roda um método em lote (-executeMethod) esperando a vez. Só um editor abre a
# mesma pasta de projeto, então vários agentes (ou terminais) que precisam do Unity passam por
# aqui: um mutex global garante um de cada vez. Antes de rodar, espera o editor do usuário
# fechar e a árvore compilar (Tools\compila.ps1), porque outro agente pode estar no meio de
# uma edição.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools\unity-fila.ps1 -Metodo BackRooms.EditorTools.Level0SmokeTest.BatchRun
#   ... -Metodo BackRooms.EditorTools.Level2PlayTest.BatchRun -Extra '-seed','1998'
#   (do bash ou com -File, também vale -Extra '-seed,1998' ou -Extra '-seeds 7,4242 -undertow')
#   ... -Metodo BackRooms.EditorTools.Level0SmokeTest.BatchHost -MetodoCopia BackRooms.EditorTools.Level0SmokeTest.BatchJoin
#
# Com -MetodoCopia, espelha Assets, Packages e ProjectSettings na cópia (-Copia, padrão
# ..\BR-rede) e abre os dois editores juntos (host aqui, cliente na cópia).
# Logs em Logs\Fila\<nome>.log (e <nome>_copia.log). Sai com o código do Unity (0 passou;
# o pior dos dois, em par), 97 se a árvore não compilou a tempo, 98 se estourou o tempo.
param(
    [string]$Metodo,
    # Em vez do Unity, roda um script do Blender na mesma fila (a máquina tem 16 GB: Unity e
    # Blender juntos derrubam um ao outro): -Blender Tools\Blender\personagens\x.py -ArgsBlender '--exportar'
    [string]$Blender,
    [string[]]$ArgsBlender = @(),
    [string]$BlenderExe = 'C:\Program Files\Blender Foundation\Blender 5.3\blender.exe',
    [string]$MetodoCopia,
    [string[]]$Extra = @(),
    [string[]]$ExtraCopia = @(),
    [string]$Nome,
    [string]$Copia,
    [int]$TimeoutMin = 45,
    [int]$EsperaCompilarMin = 15,
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'

# Chamado pelo bash ou por `powershell -File`, `-Extra '-seed','7'` chega como um texto só
# ("-seed,7"). Normaliza as duas formas: quebra tudo em pedaços (vírgula ou espaço) e junta de
# novo cada opção (pedaço que começa com '-' e não é número) com os valores que vêm depois dela,
# separados por vírgula. Assim '-seeds','7,4242', '-seeds,7,4242' e '-seeds 7,4242' dão o mesmo:
# "-seeds" "7,4242". Opção sem valor (-undertow) fica sozinha.
function NormalizaExtra([string[]]$lista) {
    $pedacos = @()
    foreach ($item in $lista) { $pedacos += ($item -split '[,\s]+' | Where-Object { $_ -ne '' }) }
    $saida = New-Object System.Collections.Generic.List[string]
    $valores = @()
    foreach ($p in $pedacos) {
        if ($p -match '^-[^\d.]') {
            if ($valores.Count -gt 0) { $saida.Add($valores -join ','); $valores = @() }
            $saida.Add($p)
        } else { $valores += $p }
    }
    if ($valores.Count -gt 0) { $saida.Add($valores -join ',') }
    return , $saida.ToArray()
}
$Extra = NormalizaExtra $Extra
$ExtraCopia = NormalizaExtra $ExtraCopia

$projeto = Split-Path -Parent $PSScriptRoot
if (-not $Copia) { $Copia = Join-Path (Split-Path -Parent $projeto) 'BR-rede' }
if (-not $Metodo -and -not $Blender) { 'Passe -Metodo (Unity) ou -Blender (script).'; exit 2 }
if (-not $Nome) {
    if ($Blender) { $Nome = 'blender_' + [IO.Path]::GetFileNameWithoutExtension($Blender) }
    else { $Nome = ($Metodo -split '\.')[-2..-1] -join '_' }
}
$logs = Join-Path $projeto 'Logs\Fila'
New-Item -ItemType Directory -Force $logs | Out-Null
$log = Join-Path $logs "$Nome.log"
$logCopia = Join-Path $logs "${Nome}_copia.log"

function EditoresAbertos([string[]]$pastas) {
    $achados = @()
    foreach ($p in (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'")) {
        $linha = ('' + $p.CommandLine).Replace('/', '\').ToLowerInvariant()
        foreach ($pasta in $pastas) {
            $alvo = $pasta.ToLowerInvariant()
            if ($linha -match [regex]::Escape($alvo) + '(["\s]|$)') { $achados += $p.ProcessId }
        }
    }
    return $achados
}

$mutex = New-Object System.Threading.Mutex($false, 'Global\BackRoomsUnityFila')
$tenho = $false
try {
    "[fila] $Nome esperando a vez..."
    try { $tenho = $mutex.WaitOne() } catch [System.Threading.AbandonedMutexException] { $tenho = $true }
    "[fila] $Nome com a vez."

    if ($Blender) {
        $ab = @('-b', '--factory-startup', '-P', $Blender)
        if ($ArgsBlender.Count -gt 0) { $ab += '--'; $ab += $ArgsBlender }
        $pb = Start-Process -FilePath $BlenderExe -ArgumentList $ab -PassThru -NoNewWindow -WorkingDirectory $projeto -RedirectStandardOutput $log -RedirectStandardError "$log.err"
        if (-not $pb.WaitForExit($TimeoutMin * 60000)) { try { $pb.Kill() } catch {}; "[fila] $Nome estourou $TimeoutMin min."; exit 98 }
        Get-Content $log -Tail 15
        "[fila] $Nome (Blender) terminou com codigo $($pb.ExitCode). Log: $log"
        exit $pb.ExitCode
    }

    $pastas = @($projeto)
    if ($MetodoCopia) { $pastas += $Copia }
    $avisou = $false
    while ((EditoresAbertos $pastas).Count -gt 0) {
        if (-not $avisou) { "[fila] Ha um editor do Unity aberto no projeto (ou na copia). Esperando fechar..."; $avisou = $true }
        Start-Sleep -Seconds 15
    }

    $limite = (Get-Date).AddMinutes($EsperaCompilarMin)
    while ($true) {
        $saida = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'compila.ps1') 2>&1
        if ($LASTEXITCODE -eq 0) { break }
        if ((Get-Date) -gt $limite) {
            "[fila] A arvore nao compila ha $EsperaCompilarMin min. Erros:"
            $saida | Select-Object -Last 25
            exit 97
        }
        "[fila] A arvore nao compila agora (outro agente editando?). Tentando de novo em 30 s."
        Start-Sleep -Seconds 30
    }

    if ($MetodoCopia) {
        foreach ($d in 'Assets', 'Packages', 'ProjectSettings') {
            & robocopy (Join-Path $projeto $d) (Join-Path $Copia $d) /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null
            if ($LASTEXITCODE -ge 8) { "[fila] robocopy falhou em $d ($LASTEXITCODE)"; exit 96 }
        }
    }

    $procs = @()
    $a = @('-batchmode', '-projectPath', $projeto, '-executeMethod', $Metodo, '-logFile', $log) + $Extra
    $procs += Start-Process -FilePath $Unity -ArgumentList $a -PassThru
    if ($MetodoCopia) {
        $b = @('-batchmode', '-projectPath', $Copia, '-executeMethod', $MetodoCopia, '-logFile', $logCopia) + $ExtraCopia
        $procs += Start-Process -FilePath $Unity -ArgumentList $b -PassThru
    }

    $fim = (Get-Date).AddMinutes($TimeoutMin)
    $estourou = $false
    foreach ($p in $procs) {
        $resta = [int][Math]::Max(1000, ($fim - (Get-Date)).TotalMilliseconds)
        if (-not $p.WaitForExit($resta)) { $estourou = $true }
    }
    if ($estourou) {
        foreach ($p in $procs) { if (-not $p.HasExited) { try { $p.Kill() } catch {} } }
        "[fila] $Nome estourou $TimeoutMin min; editores encerrados. Log: $log"
        exit 98
    }

    $codigo = 0
    foreach ($p in $procs) { if ($p.ExitCode -ne 0 -and $codigo -eq 0) { $codigo = $p.ExitCode } }
    foreach ($l in @($log, $logCopia)) {
        if (Test-Path $l) {
            "[fila] --- $l"
            Select-String -Path $l -Pattern '\[BackRooms\].*(PASSOU|FALHOU|Teste|Confer)|error CS\d+' | Select-Object -Last 12 | ForEach-Object { $_.Line }
        }
    }
    "[fila] $Nome terminou com codigo $codigo."
    exit $codigo
}
finally {
    if ($tenho) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
