# Publica uma versão nova no GitHub (Releases). Os amigos recebem sozinhos.
# Uso:  .\publish.ps1 "o que mudou nesta versão"
# Precisa do GitHub CLI instalado e logado (gh auth login).
param([string]$Notes = "")
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$repo = 'djotabr/CrosshairOverlay'

& .\build.ps1
if (-not (Test-Path CrosshairOverlay.exe)) { throw "build falhou" }
$build = [regex]::Match((Get-Content src\BuildInfo.cs -Raw), 'Build = (\d+)').Groups[1].Value

# Assina o exe com a chave privada (fica só neste PC)
$priv = Join-Path $env:APPDATA 'CrosshairOverlay\update-private.xml'
$exe = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'CrosshairOverlay.exe'))
$shaBytes = [Security.Cryptography.SHA256]::Create().ComputeHash($exe)
$sha = ([BitConverter]::ToString($shaBytes) -replace '-', '').ToLowerInvariant()
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider (New-Object Security.Cryptography.CspParameters 24)
$rsa.FromXmlString([IO.File]::ReadAllText($priv))
$sig = [Convert]::ToBase64String($rsa.SignHash($shaBytes, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')))
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'CrosshairOverlay.sig'), "$build`n$sha`n$sig")

$when = [DateTime]::ParseExact($build, 'yyyyMMddHHmm', $null).ToLocalTime().ToString('dd/MM/yyyy HH:mm')
if (-not $Notes) { $Notes = "Versão de $when" }
gh release create "v$build" CrosshairOverlay.exe CrosshairOverlay.sig --repo $repo --title "Versão $when" --notes $Notes
if ($LASTEXITCODE -eq 0) { Write-Host "Publicado: https://github.com/$repo/releases/tag/v$build" }
