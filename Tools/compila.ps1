# Compila os scripts do jogo SEM abrir o Unity, com o compilador que vem com o editor e as
# mesmas referências que o editor usou da última vez (arquivos .rsp em Library/Bee). Leva
# segundos e pode rodar com o editor aberto ou com outro agente trabalhando. Não gera nada no
# projeto e não substitui os testes: só diz se compila.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools\compila.ps1            (jogo + editor)
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools\compila.ps1 -SoJogo    (só Assembly-CSharp)
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools\compila.ps1 -Avisos    (mostra os avisos)
#
# Sai com 0 se compilou, 1 se houve erro. Precisa que o projeto já tenha sido aberto uma vez
# no Unity (é de lá que vêm as referências). Pacote novo no manifest só entra depois de o
# editor abrir de novo.
param(
    [string]$Projeto = (Split-Path -Parent $PSScriptRoot),
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Data',
    [switch]$SoJogo,
    [switch]$Avisos
)

$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $Unity 'NetCoreRuntime\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = Join-Path $Unity 'DotNetSdk\dotnet.exe' }
$csc = Get-ChildItem (Join-Path $Unity 'DotNetSdk\sdk') -Recurse -Filter csc.dll | Select-Object -First 1
$dag = Get-ChildItem (Join-Path $Projeto 'Library\Bee\artifacts') -Directory -Filter '*.dag' | Select-Object -First 1
if (-not $csc -or -not $dag) { 'Nao achei o compilador do Unity ou a pasta Library/Bee: abra o projeto no editor uma vez.'; exit 1 }
$saida = Join-Path $env:TEMP ('br_compila_' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $saida | Out-Null

function Compila([string]$nome, [string[]]$fontes, [string]$jogoDll) {
    $linhas = New-Object System.Collections.Generic.List[string]
    foreach ($l in (Get-Content (Join-Path $dag.FullName "$nome.rsp"))) {
        if ($l -match '\.cs"?$') { continue }
        if ($l -match '^-out:|^-refout:|^-analyzer:|^-additionalfile:|^-analyzerconfig:|^-ruleset:') { continue }
        if ($jogoDll -and $l -match 'Assembly-CSharp(\.ref)?\.dll') { continue }
        $linhas.Add($l)
    }
    $linhas.Add("-out:`"$saida\$nome.dll`"")
    if ($jogoDll) { $linhas.Add("-r:`"$jogoDll`"") }
    foreach ($f in $fontes) { $linhas.Add("`"$f`"") }
    $arquivo = Join-Path $saida "$nome.rsp"
    [System.IO.File]::WriteAllLines($arquivo, $linhas)

    Push-Location $Projeto
    try { $out = & $dotnet $csc.FullName "@$arquivo" 2>&1 } finally { Pop-Location }
    $codigo = $LASTEXITCODE
    $erros = $out | Where-Object { $_ -match ': error ' }
    $listaAvisos = $out | Where-Object { $_ -match ': warning ' }
    if ($erros) { $erros | ForEach-Object { "$_" } }
    if ($Avisos -and $listaAvisos) { $listaAvisos | ForEach-Object { "$_" } }
    '{0}: {1} erro(s), {2} aviso(s), {3} arquivo(s)' -f $nome, @($erros).Count, @($listaAvisos).Count, $fontes.Count
    if ($codigo -ne 0 -and -not $erros) { $out | Select-Object -Last 15 | ForEach-Object { "$_" } }
    return $codigo
}

$todos = Get-ChildItem (Join-Path $Projeto 'Assets') -Recurse -Filter *.cs | ForEach-Object { $_.FullName.Substring($Projeto.Length + 1).Replace('\', '/') }
$editor = $todos | Where-Object { $_ -match '/Editor/' }
$jogo = $todos | Where-Object { $_ -notmatch '/Editor/' }

$r = Compila 'Assembly-CSharp' $jogo $null
$codigo = $r[-1]; $r[0..($r.Count - 2)]
if ($codigo -eq 0 -and -not $SoJogo) {
    $r = Compila 'Assembly-CSharp-Editor' $editor "$saida\Assembly-CSharp.dll"
    $codigo = $r[-1]; $r[0..($r.Count - 2)]
}
Remove-Item $saida -Recurse -Force -ErrorAction SilentlyContinue
exit ([int]($codigo -ne 0))
