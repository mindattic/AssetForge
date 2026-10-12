using System.Text.Json;
using MindAttic.AssetForge.Background;
using MindAttic.AssetForge.Crop;
using MindAttic.AssetForge.Mesh;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0])
    {
        case "clean": return RunClean(args[1..]);
        case "crop": return RunCrop(args[1..]);
        case "mesh": return RunMesh(args[1..]);
        default:
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"assetforge: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        usage:
          assetforge clean <input.png> <output.png> [--fuzz 3] [--hole-area 2] [--mode Auto|BorderOnly|Skip] [--border-width 12] [--dilate 2]
          assetforge crop  <input.png> [--kind Face|Figure]
          assetforge mesh normalize <input> <output.gltf> [--yaw 0] [--target-length 1] [--axis LargestDimension|X|Y|Z] [--no-recenter] [--no-scale]
            (writes <output.gltf> plus a sibling <output>.bin -- see MeshNormalizer.Normalize's doc comment for why not a single .glb)
        """);
}

static string? OptionalArg(string[] rest, string flag)
{
    var i = Array.IndexOf(rest, flag);
    return i < 0 || i + 1 >= rest.Length ? null : rest[i + 1];
}

static int RunClean(string[] rest)
{
    if (rest.Length < 2) { PrintUsage(); return 1; }
    var inputPath = rest[0];
    var outputPath = rest[1];
    var flags = rest[2..];

    var options = new BackgroundCleanOptions
    {
        FuzzPercent = double.Parse(OptionalArg(flags, "--fuzz") ?? "3"),
        HoleAreaPercent = double.Parse(OptionalArg(flags, "--hole-area") ?? "2"),
        BorderSampleWidth = int.Parse(OptionalArg(flags, "--border-width") ?? "12"),
        DilatePixels = int.Parse(OptionalArg(flags, "--dilate") ?? "2"),
        Mode = Enum.Parse<BackgroundMode>(OptionalArg(flags, "--mode") ?? "Auto", ignoreCase: true),
    };

    using var source = Image.Load<Rgba32>(inputPath);
    var result = BackgroundCleaner.Clean(source, options);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    result.Image.SaveAsPng(outputPath);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        knockout = result.KnockoutApplied,
        originalWidth = result.OriginalSize.Width,
        originalHeight = result.OriginalSize.Height,
        trimmedWidth = result.TrimmedSize.Width,
        trimmedHeight = result.TrimmedSize.Height,
    }));
    return 0;
}

static int RunMesh(string[] rest)
{
    if (rest.Length < 1 || rest[0] != "normalize" || rest.Length < 3)
    {
        PrintUsage();
        return 1;
    }
    var inputPath = rest[1];
    var outputPath = rest[2];
    var flags = rest[3..];

    var options = new MeshNormalizeOptions
    {
        YawDegrees = float.Parse(OptionalArg(flags, "--yaw") ?? "0"),
        TargetLengthUnits = Array.IndexOf(flags, "--no-scale") >= 0
            ? null
            : float.Parse(OptionalArg(flags, "--target-length") ?? "1"),
        Axis = Enum.Parse<NormalizeAxis>(OptionalArg(flags, "--axis") ?? "LargestDimension", ignoreCase: true),
        Recenter = Array.IndexOf(flags, "--no-recenter") < 0,
    };

    MeshNormalizer.Normalize(inputPath, outputPath, options);
    Console.WriteLine(JsonSerializer.Serialize(new { normalized = true, outputPath }));
    return 0;
}

static int RunCrop(string[] rest)
{
    if (rest.Length < 1) { PrintUsage(); return 1; }
    var inputPath = rest[0];
    var flags = rest[1..];
    var kind = OptionalArg(flags, "--kind") ?? "Face";

    using var image = Image.Load<Rgba32>(inputPath);
    var box = kind.Equals("Figure", StringComparison.OrdinalIgnoreCase)
        ? PortraitCropper.FigureCrop(image)
        : PortraitCropper.FaceCrop(image);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        left = box.Left,
        top = box.Top,
        right = box.Right,
        bottom = box.Bottom,
        imageWidth = image.Width,
        imageHeight = image.Height,
    }));
    return 0;
}
