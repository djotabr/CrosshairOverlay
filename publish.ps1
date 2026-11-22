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
$ver = [regex]::Match((Get-Content src\AssemblyInfo.cs -Raw), 'Version = "([^"]+)"').Groups[1].Value

# Assina o exe com a chave privada (fica só neste PC)
$priv = Join-Path $env:APPDATA 'CrosshairOverlay\update-private.xml'
$exe = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'CrosshairOverlay.exe'))
$shaBytes = [Security.Cryptography.SHA256]::Create().ComputeHash($exe)
$sha = ([BitConverter]::ToString($shaBytes) -replace '-', '').ToLowerInvariant()
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider (New-Object Security.Cryptography.CspParameters 24)
$rsa.FromXmlString([IO.File]::ReadAllText($priv))
$sig = [Convert]::ToBase64String($rsa.SignHash($shaBytes, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')))
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'CrosshairOverlay.sig'), "$build`n$sha`n$sig`n$ver")

$when = [DateTime]::ParseExact($build, 'yyyyMMddHHmm', $null).ToLocalTime().ToString('dd/MM/yyyy HH:mm')
if (-not $Notes) { $Notes = "Versão $ver de $when" }
# tag v2.0.0; se essa versão já foi publicada antes, v2.0.0-<build>
$tag = "v$ver"
$ErrorActionPreference = 'Continue'
gh release view $tag --repo $repo 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) { $tag = "v$ver-$build" }
# notas vão por arquivo: o PowerShell 5 quebra textos com aspas ao passar para programas
$notesFile = Join-Path $env:TEMP "crosshair-release-notes.md"
[IO.File]::WriteAllText($notesFile, $Notes, (New-Object Text.UTF8Encoding $false))
gh release create $tag CrosshairOverlay.exe CrosshairOverlay.sig --repo $repo --title "Crosshair Overlay $ver ($when)" --notes-file $notesFile
Remove-Item $notesFile -ErrorAction SilentlyContinue
if ($LASTEXITCODE -eq 0) { Write-Host "Publicado: https://github.com/$repo/releases/tag/$tag" }
