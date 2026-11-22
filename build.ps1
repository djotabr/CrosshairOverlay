# Compila o Crosshair Overlay usando o compilador C# que já vem com o Windows (.NET Framework 4.x).
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Set-Location $PSScriptRoot
if (-not (Test-Path app.ico)) { & powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 }

# Chave de assinatura das atualizações: fica SÓ neste PC (%APPDATA%\CrosshairOverlay\update-private.xml).
# Não apague nem compartilhe: sem ela os amigos não aceitam versões novas (teriam que reinstalar na mão).
$keyDir = Join-Path $env:APPDATA 'CrosshairOverlay'
$priv = Join-Path $keyDir 'update-private.xml'
if (-not (Test-Path $priv)) {
    New-Item -ItemType Directory -Force $keyDir | Out-Null
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider 2048
    [IO.File]::WriteAllText($priv, $rsa.ToXmlString($true))
}
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString([IO.File]::ReadAllText($priv))
$pub = $rsa.ToXmlString($false)
$build = [DateTime]::UtcNow.ToString('yyyyMMddHHmm')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'src\BuildInfo.cs'),
    "// Gerado pelo build.ps1 — não editar.`r`nstatic class BuildInfo`r`n{`r`n    public const long Build = $build;`r`n    public const string UpdateKey = `"$pub`";`r`n}`r`n")
& $csc /nologo /target:winexe /codepage:65001 /optimize+ /out:CrosshairOverlay.exe `
    /win32icon:app.ico /win32manifest:app.manifest `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    src\*.cs
if ($LASTEXITCODE -eq 0) { Write-Host "OK -> CrosshairOverlay.exe" }
