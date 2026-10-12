using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MindAttic.AssetForge.Background;

/// <summary>How an enclosed (non-border-touching) blob of background-colored pixels is handled.</summary>
public enum BackgroundMode
{
    /// <summary>Border-connected background is always erased; an enclosed blob is only erased if it's
    /// small relative to the image (a gap between legs, a hole in a cape) — not a creature's whole pale
    /// shell interior, which forms a large enclosed blob and must stay opaque.</summary>
    Auto,

    /// <summary>Only the blob connected to the image's outer border is erased. Safe default for art
    /// whose own palette reads too close to the sampled background for the area cutoff to tell apart.</summary>
    BorderOnly,

    /// <summary>Leave the file completely untouched (for manual fixing).</summary>
    Skip,
}

public sealed record BackgroundCleanOptions
{
    /// <summary>Color-distance tolerance (as a percent of the 0-255 channel range) for a pixel to count
    /// as "background-colored"; also controls the near-0/near-1 alpha snap dead-zone.</summary>
    public double FuzzPercent { get; init; } = 3.0;

    /// <summary>An enclosed blob not touching the border is eligible for erasure only if its area is at
    /// most this percent of the total image area.</summary>
    public double HoleAreaPercent { get; init; } = 2.0;

    /// <summary>Width, in pixels, of the border frame sampled to estimate the background color.</summary>
    public int BorderSampleWidth { get; init; } = 12;

    /// <summary>How far the eligible-erasure mask is grown (in pixels) to catch the anti-aliased fringe
    /// just outside the tight color-distance threshold.</summary>
    public int DilatePixels { get; init; } = 2;

    public BackgroundMode Mode { get; init; } = BackgroundMode.Auto;
}

public sealed record BackgroundCleanResult(
    Image<Rgba32> Image,
    bool KnockoutApplied,
    Size OriginalSize,
    Size TrimmedSize);

/// <summary>
/// Background/whitespace cleanup via Color-to-Alpha un-mixing (GIMP's technique): a continuous per-pixel
/// alpha computed by inverting <c>pixel = alpha*fg + (1-alpha)*bg</c>, instead of a binary flood-fill
/// knockout — flood-fill discards the fractional anti-aliasing coverage at the figure's edge and produces
/// stair-stepped ("jaggy") edges.
/// </summary>
public static class BackgroundCleaner
{
    public static BackgroundCleanResult Clean(Image<Rgba32> source, BackgroundCleanOptions options)
    {
        var originalSize = source.Size();

        if (options.Mode == BackgroundMode.Skip)
            return new BackgroundCleanResult(source.Clone(), false, originalSize, originalSize);

        if (HasExistingAlpha(source))
        {
            var trimmedOnly = TrimToAlphaBounds(source.Clone());
            return new BackgroundCleanResult(trimmedOnly, false, originalSize, trimmedOnly.Size());
        }

        var width = source.Width;
        var height = source.Height;
        var bg = SampleBackgroundColor(source, options.BorderSampleWidth);
        var threshold = Math.Max(10.0, options.FuzzPercent * 10.0);

        var candidate = new bool[width, height];
        source.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                    candidate[x, y] = ColorDistance(row[x], bg) < threshold;
            }
        });

        var labels = ConnectedComponentLabeler.Label(candidate, width, height);

        bool IsEligible(int x, int y)
        {
            if (!candidate[x, y]) return false;
            var id = labels.LabelAt[x, y];
            if (options.Mode == BackgroundMode.BorderOnly)
                return labels.TouchesBorder[id];
            var holeAreaPixels = width * (double)height * options.HoleAreaPercent / 100.0;
            return labels.TouchesBorder[id] || labels.Size[id] <= holeAreaPixels;
        }

        var eligible = new bool[width, height];
        var anyEligible = false;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            eligible[x, y] = IsEligible(x, y);
            anyEligible |= eligible[x, y];
        }

        eligible = Dilate(eligible, width, height, options.DilatePixels);

        var snapFraction = options.FuzzPercent / 100.0;
        var result = source.Clone();
        result.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    if (!eligible[x, y])
                    {
                        var opaque = row[x];
                        row[x] = new Rgba32(opaque.R, opaque.G, opaque.B, 255);
                        continue;
                    }
                    row[x] = ColorToAlpha(row[x], bg, snapFraction);
                }
            }
        });

        var trimmed = TrimToAlphaBounds(result);
        return new BackgroundCleanResult(trimmed, anyEligible, originalSize, trimmed.Size());
    }

    public static Rgba32 SampleBackgroundColor(Image<Rgba32> image, int borderWidth)
    {
        var width = image.Width;
        var height = image.Height;
        var w = Math.Max(1, Math.Min(borderWidth, Math.Min(width, height) / 2));
        var counts = new Dictionary<(byte, byte, byte), int>();

        void Count(int x, int y)
        {
            var p = image[x, y];
            var key = (p.R, p.G, p.B);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        for (var y = 0; y < height; y++)
        {
            if (y < w || y >= height - w)
            {
                for (var x = 0; x < width; x++) Count(x, y);
            }
            else
            {
                for (var x = 0; x < w; x++) Count(x, y);
                for (var x = width - w; x < width; x++) Count(x, y);
            }
        }

        var mode = counts.OrderByDescending(kv => kv.Value).First().Key;
        return new Rgba32(mode.Item1, mode.Item2, mode.Item3, 255);
    }

    private static bool HasExistingAlpha(Image<Rgba32> image)
    {
        var found = false;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height && !found; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A is > 0 and < 255) { found = true; break; }
                }
            }
        });
        return found;
    }

    private static double ColorDistance(Rgba32 a, Rgba32 b)
    {
        double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    /// <summary>Inverts <c>pixel = alpha*fg + (1-alpha)*bg</c> per channel, guarded against
    /// divide-by-near-zero when a background channel is already near an extreme (0 or 255), with a snap
    /// dead-zone absorbing compression speckle in flat regions.</summary>
    internal static Rgba32 ColorToAlpha(Rgba32 pixel, Rgba32 bg, double snapFraction)
    {
        double Channel(byte p, byte b)
        {
            if (p >= b)
            {
                var denom = 255.0 - b;
                return denom > 2.0 ? (p - b) / denom : (p > b + 2 ? 1.0 : 0.0);
            }
            else
            {
                var denom = (double)b;
                return denom > 2.0 ? (b - p) / denom : (p < b - 2 ? 1.0 : 0.0);
            }
        }

        var aR = Channel(pixel.R, bg.R);
        var aG = Channel(pixel.G, bg.G);
        var aB = Channel(pixel.B, bg.B);
        var alpha = Math.Clamp(Math.Max(aR, Math.Max(aG, aB)), 0.0, 1.0);

        if (alpha < snapFraction) alpha = 0.0;
        else if (alpha > 1.0 - snapFraction) alpha = 1.0;

        if (alpha <= 0.0) return new Rgba32(pixel.R, pixel.G, pixel.B, 0);
        if (alpha >= 1.0) return new Rgba32(pixel.R, pixel.G, pixel.B, 255);

        byte Fg(byte p, byte b) => (byte)Math.Clamp((p - (1 - alpha) * b) / alpha, 0.0, 255.0);
        return new Rgba32(Fg(pixel.R, bg.R), Fg(pixel.G, bg.G), Fg(pixel.B, bg.B), (byte)Math.Round(alpha * 255.0));
    }

    private static bool[,] Dilate(bool[,] mask, int width, int height, int iterations)
    {
        var current = mask;
        for (var i = 0; i < iterations; i++)
        {
            var next = new bool[width, height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (current[x, y]) { next[x, y] = true; continue; }
                var grown = false;
                for (var dy = -1; dy <= 1 && !grown; dy++)
                for (var dx = -1; dx <= 1 && !grown; dx++)
                {
                    var nx = x + dx; var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    if (current[nx, ny]) grown = true;
                }
                next[x, y] = grown;
            }
            current = next;
        }
        return current;
    }

    private static Image<Rgba32> TrimToAlphaBounds(Image<Rgba32> image)
    {
        var bbox = AlphaBoundingBox(image);
        if (bbox is null) return image;
        image.Mutate(x => x.Crop(bbox.Value));
        return image;
    }

    internal static Rectangle? AlphaBoundingBox(Image<Rgba32> image)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        });
        if (maxX < 0) return null;
        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}

internal static class ConnectedComponentLabeler
{
    public readonly record struct Labels(int[,] LabelAt, List<bool> TouchesBorder, List<int> Size);

    public static Labels Label(bool[,] candidate, int width, int height)
    {
        var labelAt = new int[width, height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            labelAt[x, y] = -1;

        var touchesBorder = new List<bool>();
        var size = new List<int>();
        var nextId = 0;

        for (var y0 = 0; y0 < height; y0++)
        for (var x0 = 0; x0 < width; x0++)
        {
            if (!candidate[x0, y0] || labelAt[x0, y0] != -1) continue;

            var id = nextId++;
            touchesBorder.Add(false);
            size.Add(0);

            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue((x0, y0));
            labelAt[x0, y0] = id;

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                size[id]++;
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1) touchesBorder[id] = true;

                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = x + dx; var ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    if (!candidate[nx, ny] || labelAt[nx, ny] != -1) continue;
                    labelAt[nx, ny] = id;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        return new Labels(labelAt, touchesBorder, size);
    }
}
