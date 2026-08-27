<#
.SYNOPSIS
    Baixa o dicionario Hunspell en_US usado pela camada L0.

.DESCRIPTION
    Os dicionarios nao sao versionados no repositorio (ver .gitignore): sao dados de
    terceiros, com licenca propria, e nao fazem parte do codigo deste projeto. Este script
    busca os dois arquivos necessarios -- en_US.aff (regras de afixo) e en_US.dic (lista de
    palavras), somando cerca de 1 MB.

    Fonte primaria: repositorio oficial de dicionarios do LibreOffice.
    Fonte reserva:  wooorm/dictionaries.

    Licenca: os dicionarios en_US derivam do SCOWL e sao distribuidos sob licenca
    permissiva (BSD/MIT). Consulte o repositorio de origem antes de redistribuir junto
    com um instalador -- ver docs/adr/0002-motor-de-correcao.md.

.EXAMPLE
    .\scripts\get-dictionaries.ps1
#>
[CmdletBinding()]
param(
    [string]$Destination,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot chega vazio quando avaliado no bloco param() no PowerShell 5.1, entao o
# destino padrao e resolvido aqui, com $MyInvocation como reserva.
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $scriptDirectory = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptDirectory)) {
        $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    $Destination = Join-Path $scriptDirectory '..\assets\dictionaries'
}

$sources = @(
    @{
        Name = 'LibreOffice/dictionaries'
        Aff  = 'https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/en_US.aff'
        Dic  = 'https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/en_US.dic'
    },
    @{
        Name = 'wooorm/dictionaries'
        Aff  = 'https://raw.githubusercontent.com/wooorm/dictionaries/main/dictionaries/en/index.aff'
        Dic  = 'https://raw.githubusercontent.com/wooorm/dictionaries/main/dictionaries/en/index.dic'
    }
)

# Tamanhos minimos plausiveis. Servem para detectar o caso em que a URL responde 200 com
# uma pagina de erro em HTML em vez do arquivo -- que gravaria lixo sem reclamar.
$minimumBytes = @{ Aff = 1KB; Dic = 200KB }

$Destination = [System.IO.Path]::GetFullPath($Destination)
$affPath = Join-Path $Destination 'en_US.aff'
$dicPath = Join-Path $Destination 'en_US.dic'

if ((Test-Path $affPath) -and (Test-Path $dicPath) -and -not $Force) {
    Write-Host "Dicionario ja presente em $Destination" -ForegroundColor Green
    Write-Host 'Use -Force para baixar de novo.'
    exit 0
}

if (-not (Test-Path $Destination)) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
}

function Get-DictionaryFile {
    param([string]$Url, [string]$OutFile, [int]$MinimumBytes)

    $temporary = "$OutFile.download"
    Invoke-WebRequest -Uri $Url -OutFile $temporary -UseBasicParsing -TimeoutSec 90

    $size = (Get-Item $temporary).Length
    if ($size -lt $MinimumBytes) {
        Remove-Item $temporary -Force
        throw "Arquivo suspeito de $Url : $size bytes, esperado ao menos $MinimumBytes."
    }

    Move-Item -Path $temporary -Destination $OutFile -Force
    return $size
}

$succeeded = $false

foreach ($source in $sources) {
    Write-Host "Tentando $($source.Name)..." -ForegroundColor Cyan

    try {
        $affSize = Get-DictionaryFile -Url $source.Aff -OutFile $affPath -MinimumBytes $minimumBytes.Aff
        $dicSize = Get-DictionaryFile -Url $source.Dic -OutFile $dicPath -MinimumBytes $minimumBytes.Dic

        Write-Host ''
        Write-Host 'Pronto.' -ForegroundColor Green
        Write-Host ("  en_US.aff  {0,8:N0} bytes" -f $affSize)
        Write-Host ("  en_US.dic  {0,8:N0} bytes" -f $dicSize)
        Write-Host "  em $Destination"
        Write-Host ''
        Write-Host "Fonte: $($source.Name)"
        Write-Host 'Reinicie o Correct English para o dicionario ser carregado.'

        $succeeded = $true
        break
    }
    catch {
        Write-Warning "$($source.Name) falhou: $($_.Exception.Message)"
    }
}

if (-not $succeeded) {
    Write-Host ''
    Write-Error @'
Nenhuma das fontes respondeu. Para instalar manualmente:

  1. Baixe en_US.aff e en_US.dic de
     https://github.com/LibreOffice/dictionaries/tree/master/en
  2. Coloque os dois em assets\dictionaries\
  3. Reinicie o app

O app funciona sem o dicionario -- ele apenas informa na janela que a camada de
ortografia esta indisponivel.
'@
    exit 1
}
