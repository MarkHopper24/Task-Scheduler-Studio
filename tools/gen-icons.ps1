param(
    [string]$Source = "C:\Users\Markh\OneDrive\Pictures\WinTasker.jpg",
    [string]$OutDir = "D:\Windows Tasker\Tasker.App\Assets"
)

Add-Type -AssemblyName System.Drawing

$code = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace IconGen
{
    public static class Program
    {
        // A pixel is treated as removable background if it is near-white AND reachable
        // from the image border (flood fill). Enclosed white areas (clock hands, the
        // checkmark) are surrounded by colour, so they are never reached and stay opaque.
        static bool NearWhite(Color c) { return c.R >= 232 && c.G >= 232 && c.B >= 232; }

        public static Bitmap LoadTrimmed(string path)
        {
            using (var src = new Bitmap(path))
            {
                int w = src.Width, h = src.Height;
                var argb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        argb.SetPixel(x, y, src.GetPixel(x, y));

                var bg = new bool[w, h];
                var stack = new Stack<int>();
                Action<int, int> push = (x, y) =>
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) return;
                    if (bg[x, y]) return;
                    if (!NearWhite(argb.GetPixel(x, y))) return;
                    bg[x, y] = true;
                    stack.Push(y * w + x);
                };
                for (int x = 0; x < w; x++) { push(x, 0); push(x, h - 1); }
                for (int y = 0; y < h; y++) { push(0, y); push(w - 1, y); }
                while (stack.Count > 0)
                {
                    int v = stack.Pop(); int x = v % w, y = v / w;
                    push(x + 1, y); push(x - 1, y); push(x, y + 1); push(x, y - 1);
                }

                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (bg[x, y]) { argb.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0)); }
                        else
                        {
                            if (x < minX) minX = x; if (x > maxX) maxX = x;
                            if (y < minY) minY = y; if (y > maxY) maxY = y;
                        }
                    }
                }

                if (maxX < minX) { return argb; }
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                var cropped = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(cropped))
                    g.DrawImage(argb, new Rectangle(0, 0, bw, bh),
                        new Rectangle(minX, minY, bw, bh), GraphicsUnit.Pixel);
                argb.Dispose();
                return cropped;
            }
        }

        // fill: when true, the longer edge of the logo maps to the full target (edge-to-edge,
        // minimal padding). When false (wide/splash banners), the logo is scaled by height.
        public static Bitmap Render(Bitmap logo, int tw, int th, bool fill, double heightFactor)
        {
            var canvas = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                double scale;
                if (fill)
                    scale = (double)Math.Min(tw, th) / Math.Max(logo.Width, logo.Height);
                else
                    scale = (th * heightFactor) / logo.Height;

                int dw = (int)Math.Round(logo.Width * scale);
                int dh = (int)Math.Round(logo.Height * scale);
                int dx = (tw - dw) / 2, dy = (th - dh) / 2;
                g.DrawImage(logo, new Rectangle(dx, dy, dw, dh));
            }
            return canvas;
        }

        public static void SavePng(Bitmap bmp, string path) { bmp.Save(path, ImageFormat.Png); }

        public static void SaveIco(Bitmap logo, string path, int[] sizes)
        {
            var frames = new List<byte[]>();
            foreach (var s in sizes)
            {
                using (var f = Render(logo, s, s, true, 1.0))
                using (var ms = new MemoryStream())
                {
                    f.Save(ms, ImageFormat.Png);
                    frames.Add(ms.ToArray());
                }
            }
            using (var fs = new FileStream(path, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);
                bw.Write((short)1);
                bw.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((short)1);
                    bw.Write((short)32);
                    bw.Write(frames[i].Length);
                    bw.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var fr in frames) bw.Write(fr);
            }
        }
    }
}
'@

Add-Type -TypeDefinition $code -ReferencedAssemblies 'System.Drawing'

$logo = [IconGen.Program]::LoadTrimmed($Source)
Write-Host "Trimmed logo: $($logo.Width)x$($logo.Height)"

function Square($name, $size) {
    $b = [IconGen.Program]::Render($logo, $size, $size, $true, 1.0)
    [IconGen.Program]::SavePng($b, (Join-Path $OutDir $name)); $b.Dispose()
}
function Banner($name, $w, $h) {
    $b = [IconGen.Program]::Render($logo, $w, $h, $false, 0.92)
    [IconGen.Program]::SavePng($b, (Join-Path $OutDir $name)); $b.Dispose()
}

# Scale buckets Windows asks for across DPIs. Generating every one removes the
# fallback gaps that make Start show a broken/placeholder icon.
$scales = @(100, 125, 150, 200, 400)

# Square logos: base edge in effective pixels (scale-100). Rendered edge-to-edge.
$squareLogos = @{
    "Square44x44Logo"   = 44
    "Square150x150Logo" = 150
    "LockScreenLogo"    = 24
    "StoreLogo"         = 50
}
foreach ($name in $squareLogos.Keys) {
    $base = $squareLogos[$name]
    foreach ($s in $scales) {
        $px = [int][math]::Round($base * $s / 100.0)
        Square "$name.scale-$s.png" $px
    }
}

# Banner logos: base width x height (scale-100). Logo scaled by height, centred.
$bannerLogos = @{
    "Wide310x150Logo" = @(310, 150)
    "SplashScreen"    = @(620, 300)
}
foreach ($name in $bannerLogos.Keys) {
    $bw = $bannerLogos[$name][0]; $bh = $bannerLogos[$name][1]
    foreach ($s in $scales) {
        $w = [int][math]::Round($bw * $s / 100.0)
        $h = [int][math]::Round($bh * $s / 100.0)
        Banner "$name.scale-$s.png" $w $h
    }
}

# Square44x44 target-size variants used by the Start All-apps list and taskbar,
# in all three plating forms (the OS picks based on theme / surface).
$targetSizes = @(16, 24, 32, 48, 256)
foreach ($t in $targetSizes) {
    Square "Square44x44Logo.targetsize-$t.png" $t
    Square "Square44x44Logo.targetsize-${t}_altform-unplated.png" $t
    Square "Square44x44Logo.targetsize-${t}_altform-lightunplated.png" $t
}

# StoreLogo (no scale qualifier) is the default 50x50 used by the package Logo.
Square "StoreLogo.png" 50

[IconGen.Program]::SaveIco($logo, (Join-Path $OutDir "AppIcon.ico"), @(16,24,32,48,64,128,256))

$pngCount = (Get-ChildItem (Join-Path $OutDir "*.png")).Count
Write-Host "Wrote $pngCount PNG variants + AppIcon.ico (7 frames)."

$logo.Dispose()
Write-Host "Done."

