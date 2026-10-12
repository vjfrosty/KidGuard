# Dot-sourced by Build.ps1. Creates a multi-size Windows icon from assets\kidguard-icon.png.
# Sizes up to 64 px are stored as 32-bit bitmaps (readable by every Windows and .NET API), larger ones as PNG.
function New-KidGuardIcon([string]$Png, [string]$Ico) {
    Add-Type -AssemblyName System.Drawing
    $source = [Drawing.Image]::FromFile($Png)
    $entries = New-Object System.Collections.Generic.List[object]
    try {
        foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
            $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [Drawing.Graphics]::FromImage($bmp)
            $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
            $g.Clear([Drawing.Color]::Transparent); $g.DrawImage($source, 0, 0, $size, $size); $g.Dispose()
            $ms = New-Object IO.MemoryStream
            if ($size -ge 128) { $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png) }
            else {
                # BITMAPINFOHEADER (height doubled for the AND mask), bottom-up BGRA rows, then an all-zero AND mask.
                $w = New-Object IO.BinaryWriter $ms
                $maskRow = [int][Math]::Ceiling($size / 32) * 4
                $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2)); $w.Write([int16]1); $w.Write([int16]32)
                $w.Write([int]0); $w.Write([int]($size * $size * 4 + $maskRow * $size)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
                $data = $bmp.LockBits((New-Object Drawing.Rectangle 0, 0, $size, $size), 'ReadOnly', ([Drawing.Imaging.PixelFormat]::Format32bppArgb))
                try {
                    $row = New-Object byte[] ($size * 4)
                    for ($y = $size - 1; $y -ge 0; $y--) {
                        [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $y * $data.Stride), $row, 0, $row.Length)
                        $w.Write($row)
                    }
                } finally { $bmp.UnlockBits($data) }
                $w.Write((New-Object byte[] ($maskRow * $size)))
                $w.Flush()
            }
            $entries.Add(@($size, $ms.ToArray()))
            $bmp.Dispose()
        }
    } finally { $source.Dispose() }
    $out = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $out
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $dim = if ($e[0] -ge 256) { 0 } else { $e[0] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$e[1].Length); $w.Write([int]$offset)
        $offset += $e[1].Length
    }
    foreach ($e in $entries) { $w.Write([byte[]]$e[1]) }
    $w.Flush()
    [IO.File]::WriteAllBytes($Ico, $out.ToArray())
}
