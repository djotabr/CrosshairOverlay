# Gera app.ico (16..256 px) desenhando uma mira estilizada.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $k = $s / 256.0

    # Fundo: quadrado arredondado escuro com borda verde
    $r = 56 * $k; $m = 6 * $k; $w = $s - 2 * $m
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $r, $r, 180, 90)
    $path.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $path.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90)
    $path.AddArc($m, $m + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF $m, $m, $w, $w
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(40, 44, 54)), ([System.Drawing.Color]::FromArgb(14, 15, 19)), 90
    $g.FillPath($bg, $path)
    $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0, 170, 105)), ([Math]::Max(1, 6 * $k))
    $g.DrawPath($border, $path)

    $c = $s / 2.0
    $green = [System.Drawing.Color]::FromArgb(0, 235, 140)
    $outline = [System.Drawing.Color]::FromArgb(0, 0, 0)

    foreach ($pass in 0, 1) {
        $extra = if ($pass -eq 0) { [Math]::Max(1.5, 10 * $k) } else { 0 }
        $col = if ($pass -eq 0) { $outline } else { $green }
        $pw = [Math]::Max(1.6, 16 * $k) + $extra
        $pen = New-Object System.Drawing.Pen $col, $pw
        # Círculo
        $cr = 74 * $k
        if ($s -ge 24) { $g.DrawEllipse($pen, [float]($c - $cr), [float]($c - $cr), [float](2 * $cr), [float](2 * $cr)) }
        # Braços (atravessam o círculo)
        $gap = 30 * $k; $len = 104 * $k
        if ($s -lt 24) { $gap = 2.5 * $s / 16; $len = 6.5 * $s / 16 }
        $g.DrawLine($pen, [float]$c, [float]($c - $len), [float]$c, [float]($c - $gap))
        $g.DrawLine($pen, [float]$c, [float]($c + $gap), [float]$c, [float]($c + $len))
        $g.DrawLine($pen, [float]($c - $len), [float]$c, [float]($c - $gap), [float]$c)
        $g.DrawLine($pen, [float]($c + $gap), [float]$c, [float]($c + $len), [float]$c)
        # Ponto central
        $d = [Math]::Max(1.5, 14 * $k) + $extra
        $b = New-Object System.Drawing.SolidBrush $col
        $g.FillEllipse($b, [float]($c - $d / 2), [float]($c - $d / 2), [float]$d, [float]$d)
        $pen.Dispose(); $b.Dispose()
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Entrada BMP clássica (32 bits BGRA + máscara AND), compatível com tudo
        $bw0 = New-Object System.IO.BinaryWriter $ms
        $bw0.Write([UInt32]40); $bw0.Write([Int32]$s); $bw0.Write([Int32]($s * 2))
        $bw0.Write([UInt16]1); $bw0.Write([UInt16]32); $bw0.Write([UInt32]0)
        $bw0.Write([UInt32]($s * $s * 4)); $bw0.Write([Int32]0); $bw0.Write([Int32]0); $bw0.Write([UInt32]0); $bw0.Write([UInt32]0)
        $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $s, $s), 'ReadOnly', ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
        $raw = New-Object byte[] ($data.Stride * $s)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
        $bmp.UnlockBits($data)
        for ($y = $s - 1; $y -ge 0; $y--) { $bw0.Write($raw, $y * $data.Stride, $s * 4) }
        $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw0.Write((New-Object byte[] ($maskRow * $s)))
        $bw0.Flush()
    }
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

# Monta o arquivo .ico (entradas PNG)
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Host "OK -> app.ico"
