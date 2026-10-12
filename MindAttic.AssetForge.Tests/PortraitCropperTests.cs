using MindAttic.AssetForge.Crop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MindAttic.AssetForge.Tests;

public class PortraitCropperTests
{
    private static Image<Rgba32> MakeFigureOnTransparent(int width, int height, Rectangle figure)
    {
        var image = new Image<Rgba32>(width, height); // defaults to fully transparent
        image.ProcessPixelRows(accessor =>
        {
            for (var y = figure.Top; y < figure.Bottom; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = figure.Left; x < figure.Right; x++)
                    row[x] = new Rgba32(10, 10, 10, 255);
            }
        });
        return image;
    }

    [Test]
    public void FaceCrop_ReturnsBoxWithinImageBounds()
    {
        using var image = MakeFigureOnTransparent(200, 400, Rectangle.FromLTRB(40, 20, 160, 380));
        var box = PortraitCropper.FaceCrop(image);

        Assert.That(box.Left, Is.GreaterThanOrEqualTo(0));
        Assert.That(box.Top, Is.GreaterThanOrEqualTo(0));
        Assert.That(box.Right, Is.LessThanOrEqualTo(image.Width));
        Assert.That(box.Width, Is.GreaterThan(0));
        Assert.That(box.Height, Is.GreaterThan(0));
    }

    [Test]
    public void FaceCrop_CentersOnFigureHorizontally()
    {
        // Figure is shifted to the right half of a wide canvas; the face crop's center should follow it,
        // not default to the full canvas's center.
        using var image = MakeFigureOnTransparent(400, 300, Rectangle.FromLTRB(250, 10, 350, 290));
        var box = PortraitCropper.FaceCrop(image);
        var centerX = (box.Left + box.Right) / 2.0;

        Assert.That(centerX, Is.GreaterThan(200), "crop center should follow the off-center figure, not the canvas center");
    }

    [Test]
    public void FigureCrop_SquaresOffTheFullBoundingBoxWithPadding()
    {
        using var image = MakeFigureOnTransparent(200, 200, Rectangle.FromLTRB(50, 50, 150, 150));
        var box = PortraitCropper.FigureCrop(image);

        Assert.That(box.Width, Is.EqualTo(box.Height), "figure crop must be square");
        Assert.That(box.Width, Is.GreaterThan(100), "should pad slightly beyond the raw 100x100 bounding box");
    }

    [Test]
    public void FaceCrop_EmptyImage_ReturnsFullCanvas()
    {
        using var image = new Image<Rgba32>(64, 64);
        var box = PortraitCropper.FaceCrop(image);
        Assert.That(box, Is.EqualTo(new CropBox(0, 0, 64, 64)));
    }
}
