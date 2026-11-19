$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$iconProjectRoot = Split-Path -Parent $PSScriptRoot
$iconSourcePath = Join-Path $iconProjectRoot 'Assets\app-icon.png'
$iconOutputPath = Join-Path $iconProjectRoot 'Assets\app-icon.ico'
$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$iconFrames = [System.Collections.Generic.List[byte[]]]::new()
$iconSource = [System.Drawing.Bitmap]::FromFile($iconSourcePath)
try {
    foreach ($iconSize in $iconSizes) {
        $iconBitmap = [System.Drawing.Bitmap]::new($iconSize, $iconSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $iconGraphics = [System.Drawing.Graphics]::FromImage($iconBitmap)
            $iconAttributes = [System.Drawing.Imaging.ImageAttributes]::new()
            try {
                $iconGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $iconGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $iconGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $iconAttributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
                $iconGraphics.DrawImage($iconSource, [System.Drawing.Rectangle]::new(0, 0, $iconSize, $iconSize), 0, 0, $iconSource.Width, $iconSource.Height, [System.Drawing.GraphicsUnit]::Pixel, $iconAttributes)
            } finally {
                $iconAttributes.Dispose()
                $iconGraphics.Dispose()
            }

            # Classic 32-bit DIB frames preserve alpha and support the Framework compiler.
            # ICO stores bottom-up color rows followed by a DWORD-aligned transparency mask.
            $iconMaskStride = [int]([Math]::Ceiling($iconSize / 32.0) * 4)
            $iconStream = [System.IO.MemoryStream]::new()
            $iconWriter = [System.IO.BinaryWriter]::new($iconStream)
            try {
                $iconWriter.Write([int]40)
                $iconWriter.Write([int]$iconSize)
                $iconWriter.Write([int]($iconSize * 2))
                $iconWriter.Write([uint16]1)
                $iconWriter.Write([uint16]32)
                $iconWriter.Write([int]0)
                $iconWriter.Write([int]($iconSize * $iconSize * 4 + $iconMaskStride * $iconSize))
                foreach ($iconZero in 1..4) { $iconWriter.Write([int]0) }
                $iconMasks = [System.Collections.Generic.List[byte[]]]::new()
                $iconBits = $iconBitmap.LockBits([System.Drawing.Rectangle]::new(0, 0, $iconSize, $iconSize), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                try {
                    for ($iconRow = $iconSize - 1; $iconRow -ge 0; $iconRow--) {
                        $iconPixels = [byte[]]::new($iconSize * 4)
                        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($iconBits.Scan0, $iconRow * $iconBits.Stride), $iconPixels, 0, $iconPixels.Length)
                        $iconWriter.Write($iconPixels, 0, $iconPixels.Length)
                        $iconMask = [byte[]]::new($iconMaskStride)
                        for ($iconColumn = 0; $iconColumn -lt $iconSize; $iconColumn++) {
                            if ($iconPixels[$iconColumn * 4 + 3] -eq 0) {
                                $iconMaskIndex = [int][Math]::Floor($iconColumn / 8.0)
                                $iconMask[$iconMaskIndex] = $iconMask[$iconMaskIndex] -bor (128 -shr ($iconColumn % 8))
                            }
                        }
                        $iconMasks.Add($iconMask)
                    }
                } finally { $iconBitmap.UnlockBits($iconBits) }
                foreach ($iconMask in $iconMasks) { $iconWriter.Write($iconMask, 0, $iconMask.Length) }
                $iconWriter.Flush()
                $iconFrames.Add($iconStream.ToArray())
            } finally { $iconWriter.Dispose() }
        } finally { $iconBitmap.Dispose() }
    }
} finally { $iconSource.Dispose() }

$iconFile = [System.IO.File]::Create($iconOutputPath)
$iconDirectory = [System.IO.BinaryWriter]::new($iconFile)
try {
    $iconDirectory.Write([uint16]0)
    $iconDirectory.Write([uint16]1)
    $iconDirectory.Write([uint16]$iconSizes.Count)
    $iconOffset = 6 + 16 * $iconSizes.Count
    for ($iconIndex = 0; $iconIndex -lt $iconSizes.Count; $iconIndex++) {
        $iconDimension = if ($iconSizes[$iconIndex] -eq 256) { 0 } else { $iconSizes[$iconIndex] }
        $iconDirectory.Write([byte]$iconDimension)
        $iconDirectory.Write([byte]$iconDimension)
        $iconDirectory.Write([byte]0)
        $iconDirectory.Write([byte]0)
        $iconDirectory.Write([uint16]1)
        $iconDirectory.Write([uint16]32)
        $iconDirectory.Write([uint32]$iconFrames[$iconIndex].Length)
        $iconDirectory.Write([uint32]$iconOffset)
        $iconOffset += $iconFrames[$iconIndex].Length
    }
    foreach ($iconFrame in $iconFrames) { $iconDirectory.Write($iconFrame, 0, $iconFrame.Length) }
} finally { $iconDirectory.Dispose() }
Write-Output ('Built icon: ' + $iconOutputPath)
