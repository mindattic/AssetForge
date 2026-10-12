using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MindAttic.AssetForge.Crop;

/// <summary>A square crop box in source-image pixel coordinates.</summary>
public readonly record struct CropBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Head/figure framing heuristics, ported from GridGameGaiden's <c>tools/import_art.py</c>
/// (<c>face_crop</c>/<c>figure_crop</c>). Generic image-domain logic only — a caller decides what to do
/// with the resulting box (e.g. deriving a game-specific crop percentage).
/// </summary>
public static class PortraitCropper
{
    /// <summary>Square crop around the head/upper body: the alpha-weighted centroid of the top 30%-height
    /// band of the figure's bounding box. Suited to humanoid/hero portraits.</summary>
    public static CropBox FaceCrop(Image<Rgba32> image)
    {
        var w = image.Width;
        var h = image.Height;
        var bbox = Background.BackgroundCleaner.AlphaBoundingBox(image);
        if (bbox is null) return new CropBox(0, 0, w, h);

        var (l, t, r, b) = (bbox.Value.Left, bbox.Value.Top, bbox.Value.Right, bbox.Value.Bottom);
        var bw = r - l;
        var bh = b - t;
        var bandHeight = Math.Max(1, (int)(bh * 0.30));

        double total = 0, acc = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = t; y < Math.Min(t + bandHeight, h); y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = l; x < r; x++)
                {
                    var a = row[x].A;
                    if (a <= 32) continue;
                    total += a;
                    acc += a * x;
                }
            }
        });

        var cx = l + (total > 0 ? acc / total : bw / 2.0);
        var size = (int)Math.Max(Math.Min(bw, bh * 0.42), w * 0.28);
        size = Math.Min(size, Math.Min(w, h));
        var top = Math.Max(0, (int)(t - size * 0.06));
        var left = Math.Clamp((int)(cx - size / 2.0), 0, w - size);
        top = Math.Min(h - size, top);

        return new CropBox(left, top, left + size, top + size);
    }

    /// <summary>Whole-figure square fit (creatures/beasts read best fully in frame): the figure's alpha
    /// bounding box, squared off and padded 4%, centered on the bounding box's own center.</summary>
    public static CropBox FigureCrop(Image<Rgba32> image)
    {
        var bbox = Background.BackgroundCleaner.AlphaBoundingBox(image);
        if (bbox is null) return new CropBox(0, 0, image.Width, image.Height);

        var (l, t, r, b) = (bbox.Value.Left, bbox.Value.Top, bbox.Value.Right, bbox.Value.Bottom);
        var size = (int)(Math.Max(r - l, b - t) * 1.04);
        var cx = (l + r) / 2.0;
        var cy = (t + b) / 2.0;
        return new CropBox((int)(cx - size / 2.0), (int)(cy - size / 2.0), (int)(cx + size / 2.0), (int)(cy + size / 2.0));
    }
}
