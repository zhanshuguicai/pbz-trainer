# Draws the manager icon (ink ring cut by a red blade slash) and writes a multi-size .ico plus a 256 px preview PNG.
# Original drawing after the user's description of the game's UI mark; no game asset is copied.
param(
    [string]$OutIco = "$PSScriptRoot\..\src\PBZManager\PBZManager\app.ico",
    [string]$OutPng = "$PSScriptRoot\..\src\PBZManager\PBZManager\app.png"
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $bg = [System.Drawing.Color]::FromArgb(255, 24, 22, 22)
    $ink = [System.Drawing.Color]::FromArgb(255, 236, 228, 212)
    $red = [System.Drawing.Color]::FromArgb(255, 200, 24, 40)

    # Dark disc so the mark reads on light and dark taskbars.
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $bg), 4 * $s, 4 * $s, 248 * $s, 248 * $s)
    # Ring.
    $ringPen = New-Object System.Drawing.Pen $ink, ([Math]::Max(2, 24 * $s))
    $g.DrawEllipse($ringPen, 54 * $s, 54 * $s, 148 * $s, 148 * $s)

    # Blade slash: a long tapered diamond from lower left to upper right, thickest a little past the hilt end.
    $ax = 30; $ay = 226; $bx = 226; $by = 30
    $dx = $bx - $ax; $dy = $by - $ay; $len = [Math]::Sqrt($dx * $dx + $dy * $dy)
    $ux = $dx / $len; $uy = $dy / $len; $px = -$uy; $py = $ux
    $t = 0.42; $w = 17
    $mx = $ax + $dx * $t; $my = $ay + $dy * $t
    $pts = @(
        (New-Object System.Drawing.PointF ($ax * $s), ($ay * $s)),
        (New-Object System.Drawing.PointF (($mx + $px * $w) * $s), (($my + $py * $w) * $s)),
        (New-Object System.Drawing.PointF ($bx * $s), ($by * $s)),
        (New-Object System.Drawing.PointF (($mx - $px * $w * 0.55) * $s), (($my - $py * $w * 0.55) * $s))
    )
    # Dark outline first so the slash visibly cuts through the ring.
    $cut = New-Object System.Drawing.Pen $bg, ([Math]::Max(1.5, 12 * $s))
    $cut.LineJoin = 'Round'
    $g.DrawPolygon($cut, [System.Drawing.PointF[]]$pts)
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $red), [System.Drawing.PointF[]]$pts)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($n in $sizes) {
    $bmp = Draw $n
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($n -eq 256) { $bmp.Save([IO.Path]::GetFullPath($OutPng), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO container with PNG-compressed entries.
$out = New-Object System.IO.MemoryStream
$wr = New-Object System.IO.BinaryWriter $out
$wr.Write([UInt16]0); $wr.Write([UInt16]1); $wr.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $data = $pngs[$i]
    $wr.Write([byte]($n % 256)); $wr.Write([byte]($n % 256)); $wr.Write([byte]0); $wr.Write([byte]0)
    $wr.Write([UInt16]1); $wr.Write([UInt16]32); $wr.Write([UInt32]$data.Length); $wr.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $wr.Write($data) }
$wr.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($OutIco), $out.ToArray())
"wrote $OutIco ($($out.Length) bytes) and $OutPng"
