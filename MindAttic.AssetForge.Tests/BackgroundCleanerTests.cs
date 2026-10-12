using MindAttic.AssetForge.Background;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MindAttic.AssetForge.Tests;

public class BackgroundCleanerTests
{
    // Regression test for the un-mixing formula's divide-by-near-zero bug: when the sampled background
    // color is already near an extreme (e.g. 254 instead of a clean 255 because of JPEG/corner noise),
    // the un-guarded formula blew up to alpha~1 (fully opaque "foreground") for genuine background
    // pixels. The guard must instead treat an at-or-past-extreme pixel as background (alpha 0).
    [Test]
    public void ColorToAlpha_NearExtremeBackground_DoesNotInvertToOpaque()
    {
        var nearWhiteBg = new Rgba32(254, 254, 254, 255);
        var purewhitePixel = new Rgba32(255, 255, 255, 255);

        var result = BackgroundCleaner.ColorToAlpha(purewhitePixel, nearWhiteBg, snapFraction: 0.03);

        Assert.That(result.A, Is.EqualTo(0), "a pixel at/beyond a near-extreme background channel must snap to transparent, not opaque");
    }

    [Test]
    public void ColorToAlpha_PixelEqualsBackground_IsFullyTransparent()
    {
        var bg = new Rgba32(255, 255, 255, 255);
        var result = BackgroundCleaner.ColorToAlpha(bg, bg, snapFraction: 0.03);
        Assert.That(result.A, Is.EqualTo(0));
    }

    [Test]
    public void ColorToAlpha_PixelOppositeOfBackground_IsFullyOpaque()
    {
        var bg = new Rgba32(255, 255, 255, 255);
        var black = new Rgba32(0, 0, 0, 255);
        var result = BackgroundCleaner.ColorToAlpha(black, bg, snapFraction: 0.03);
        Assert.That(result.A, Is.EqualTo(255));
    }

    // Regression test for the "ant-monster" bug: a creature whose own shell/body is pale and close to the
    // sampled background color must not have its interior erased just because it forms an enclosed blob
    // of background-colored pixels — only a *small* enclosed blob (a true hole/gap) should erase.
    [Test]
    public void Clean_LargeEnclosedPaleRegion_StaysOpaque()
    {
        const int size = 100;
        using var image = new Image<Rgba32>(size, size);
        var white = new Rgba32(255, 255, 255, 255);
        var darkOutline = new Rgba32(40, 20, 10, 255);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < size; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < size; x++)
                    row[x] = white; // whole canvas starts as background
            }
        });

        // A dark ring from (20,20) to (80,80), 4px thick, enclosing a large (52x52) pale interior —
        // stands in for the ant-monster's own pale shell, bounded by its outline stroke. The interior is
        // left at the base white fill (same color as the outer background), same as a real pale shell.
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 20; y < 80; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 20; x < 80; x++)
                {
                    var onRing = x < 24 || x >= 76 || y < 24 || y >= 76;
                    if (onRing) row[x] = darkOutline;
                }
            }
        });

        var result = BackgroundCleaner.Clean(image, new BackgroundCleanOptions { HoleAreaPercent = 2.0 });

        Assert.That(result.KnockoutApplied, Is.True);
        // The outer background (everything outside the dark ring) must have been erased and trimmed away
        // — the surviving (trimmed) canvas should shrink down to roughly the ring's own 60x60 footprint,
        // not the original 100x100.
        Assert.That(result.TrimmedSize.Width, Is.LessThan(result.OriginalSize.Width));
        Assert.That(result.TrimmedSize.Height, Is.LessThan(result.OriginalSize.Height));

        // Map original coordinates into the trimmed/cropped result image (crop origin is the dark ring's
        // own bbox, i.e. (20,20) in the original canvas).
        var interiorPixel = result.Image[50 - 20, 50 - 20]; // large pale interior
        Assert.That(interiorPixel.A, Is.EqualTo(255), "a large pale interior region must not be erased");
    }

    [Test]
    public void Clean_SmallEnclosedHoleInOpaqueSubject_Erases()
    {
        const int size = 100;
        using var image = new Image<Rgba32>(size, size);
        var white = new Rgba32(255, 255, 255, 255);
        var darkSubject = new Rgba32(40, 20, 10, 255);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < size; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < size; x++)
                    row[x] = white; // outer background
            }
        });

        // A solid dark block (a "cape") from (20,20) to (80,80), with a small (3x3) white hole punched
        // through its middle — a true gap in the artwork, fully surrounded by non-background-colored
        // subject (unlike the pale-shell case above, where the "hole" is really just more subject).
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 20; y < 80; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 20; x < 80; x++)
                    row[x] = darkSubject;
            }
        });
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 48; y < 51; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 48; x < 51; x++)
                    row[x] = white;
            }
        });

        var result = BackgroundCleaner.Clean(image, new BackgroundCleanOptions { HoleAreaPercent = 2.0 });

        var holePixel = result.Image[49 - 20, 49 - 20];
        Assert.That(holePixel.A, Is.EqualTo(0), "a small enclosed hole fully inside the subject must still be erased");

        var subjectPixel = result.Image[30 - 20, 30 - 20];
        Assert.That(subjectPixel.A, Is.EqualTo(255), "the surrounding subject must stay opaque");
    }

    [Test]
    public void Clean_BorderOnlyMode_NeverErasesEnclosedBlobs()
    {
        const int size = 40;
        using var image = new Image<Rgba32>(size, size);
        var white = new Rgba32(255, 255, 255, 255);
        var darkOutline = new Rgba32(40, 20, 10, 255);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < size; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < size; x++)
                    row[x] = white;
            }
        });

        // A thin dark ring around a tiny (2x2) enclosed pale hole — small enough that Auto mode would
        // erase it, but BorderOnly must not.
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 10; y < 20; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 10; x < 20; x++)
                {
                    var onRing = x < 13 || x >= 17 || y < 13 || y >= 17;
                    if (onRing) row[x] = darkOutline;
                }
            }
        });

        var result = BackgroundCleaner.Clean(image, new BackgroundCleanOptions { Mode = BackgroundMode.BorderOnly });

        var holePixel = result.Image[14 - 10, 14 - 10];
        Assert.That(holePixel.A, Is.EqualTo(255), "BorderOnly mode must leave enclosed blobs untouched regardless of size");
    }

    [Test]
    public void Clean_SkipMode_LeavesImageUntouched()
    {
        using var image = new Image<Rgba32>(10, 10);
        var result = BackgroundCleaner.Clean(image, new BackgroundCleanOptions { Mode = BackgroundMode.Skip });
        Assert.That(result.KnockoutApplied, Is.False);
        Assert.That(result.TrimmedSize, Is.EqualTo(result.OriginalSize));
    }

    [Test]
    public void SampleBackgroundColor_ReturnsModeOfBorderFrame()
    {
        const int size = 20;
        using var image = new Image<Rgba32>(size, size);
        var bg = new Rgba32(250, 251, 249, 255);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < size; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < size; x++) row[x] = bg;
            }
        });
        var sampled = BackgroundCleaner.SampleBackgroundColor(image, 4);
        Assert.That(sampled, Is.EqualTo(bg));
    }
}
