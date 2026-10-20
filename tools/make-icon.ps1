<#
.SYNOPSIS
    把一张 PNG 转成多尺寸 .ico（16/24/32/40/48/64/128/256），给 WinForms/WPF 程序当图标。

.EXAMPLE
    .\make-icon.ps1 -Png ..\..\icon.png -Out ..\src\MAAConfigLauncher\app.ico

.NOTES
    - 源图建议 ≥256×256；小图（如 40×40）放大后会糊。
    - 用未压缩的 DIB 条目写 ICO（不用 PNG 压缩），这样 System.Drawing.Icon 也能读。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Png,
    [Parameter(Mandatory = $true)][string]$Out,
    [int[]]$Sizes = @(16, 24, 32, 40, 48, 64, 128, 256)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IcoBuilder
{
    static byte[] DibBytes(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = ((w + 31) / 32) * 4;
        int maskSize = maskStride * h;
        int pixelSize = w * h * 4;

        using (var ms = new MemoryStream(40 + pixelSize + maskSize))
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(pixelSize);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[w * 4];
                for (int y = h - 1; y >= 0; y--)
                {
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    bw.Write(row);
                }
            }
            finally { bmp.UnlockBits(data); }

            bw.Write(new byte[maskSize]);
            bw.Flush();
            return ms.ToArray();
        }
    }

    public static void Build(string srcPng, string outIco, int[] sizes)
    {
        var blobs = new List<byte[]>();
        using (var src = new Bitmap(srcPng))
        {
            foreach (int s in sizes)
            {
                using (var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        // 放大用最近邻，保住像素画的硬边；缩小用双三次，边缘更干净
                        g.InterpolationMode = s >= src.Width ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.SmoothingMode = SmoothingMode.None;
                        g.DrawImage(src, new Rectangle(0, 0, s, s));
                    }
                    blobs.Add(DibBytes(bmp));
                }
            }
        }

        using (var fs = new FileStream(outIco, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32);
                bw.Write(blobs[i].Length); bw.Write(offset);
                offset += blobs[i].Length;
            }
            foreach (var b in blobs) bw.Write(b);
        }
    }
}
'@

[IcoBuilder]::Build($Png, $Out, $Sizes)
$f = Get-Item $Out
Write-Host ("生成 {0}（{1:N0} 字节，{2} 个尺寸）" -f $f.FullName, $f.Length, $Sizes.Count)
