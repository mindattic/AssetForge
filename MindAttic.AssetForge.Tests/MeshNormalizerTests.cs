using Assimp;
using MindAttic.AssetForge.Mesh;

namespace MindAttic.AssetForge.Tests;

public class MeshNormalizerTests
{
    // A 2x2x2 cube offset far from the origin, standing in for a raw asset-pack model with its own
    // arbitrary layout offset and scale — exactly what ShipModels.cs's Normalise() fixes up at runtime.
    private static Scene MakeOffsetCubeScene(Vector3D offset, float halfSize = 1f)
    {
        var scene = new Scene { RootNode = new Node("root") };
        var mesh = new Assimp.Mesh("cube", PrimitiveType.Triangle);
        Vector3D[] corners =
        {
            new(-halfSize, -halfSize, -halfSize), new(halfSize, -halfSize, -halfSize),
            new(halfSize, halfSize, -halfSize), new(-halfSize, halfSize, -halfSize),
            new(-halfSize, -halfSize, halfSize), new(halfSize, -halfSize, halfSize),
            new(halfSize, halfSize, halfSize), new(-halfSize, halfSize, halfSize),
        };
        foreach (var c in corners) mesh.Vertices.Add(c + offset);
        int[][] faces =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
            new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 },
            new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 0, 3, 7 }, new[] { 0, 7, 4 },
        };
        foreach (var f in faces) mesh.Faces.Add(new Face(f));
        mesh.MaterialIndex = 0;
        scene.Meshes.Add(mesh);
        scene.RootNode.MeshIndices.Add(0);
        scene.Materials.Add(new Material { Name = "default" });
        return scene;
    }

    [Test]
    public void ComputeWorldBounds_ReturnsOffsetCubeBounds()
    {
        var scene = MakeOffsetCubeScene(new Vector3D(100, 50, -30), halfSize: 2f);
        var (min, max) = MeshNormalizer.ComputeWorldBounds(scene);

        Assert.That(min.X, Is.EqualTo(98f).Within(1e-4));
        Assert.That(max.X, Is.EqualTo(102f).Within(1e-4));
        Assert.That(min.Y, Is.EqualTo(48f).Within(1e-4));
        Assert.That(max.Y, Is.EqualTo(52f).Within(1e-4));
    }

    [Test]
    public void NormalizeScene_RecentersOnOrigin()
    {
        var scene = MakeOffsetCubeScene(new Vector3D(100, 50, -30));
        MeshNormalizer.NormalizeScene(scene, new MeshNormalizeOptions { TargetLengthUnits = null, Recenter = true });

        var (min, max) = MeshNormalizer.ComputeWorldBounds(scene);
        var center = (min + max) * 0.5f;
        Assert.That(center.X, Is.EqualTo(0f).Within(1e-3));
        Assert.That(center.Y, Is.EqualTo(0f).Within(1e-3));
        Assert.That(center.Z, Is.EqualTo(0f).Within(1e-3));
    }

    [Test]
    public void NormalizeScene_RescalesLargestDimensionToTargetLength()
    {
        // A non-cubic box (4x2x2) so "largest dimension" is unambiguous.
        var scene = new Scene { RootNode = new Node("root") };
        var mesh = new Assimp.Mesh("box", PrimitiveType.Triangle);
        Vector3D[] corners =
        {
            new(-2, -1, -1), new(2, -1, -1), new(2, 1, -1), new(-2, 1, -1),
            new(-2, -1, 1), new(2, -1, 1), new(2, 1, 1), new(-2, 1, 1),
        };
        foreach (var c in corners) mesh.Vertices.Add(c);
        int[][] faces =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
            new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 },
            new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 0, 3, 7 }, new[] { 0, 7, 4 },
        };
        foreach (var f in faces) mesh.Faces.Add(new Face(f));
        mesh.MaterialIndex = 0;
        scene.Meshes.Add(mesh);
        scene.RootNode.MeshIndices.Add(0);
        scene.Materials.Add(new Material { Name = "default" });

        MeshNormalizer.NormalizeScene(scene, new MeshNormalizeOptions { TargetLengthUnits = 1f, Recenter = false });

        var (min, max) = MeshNormalizer.ComputeWorldBounds(scene);
        var size = max - min;
        var largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
        Assert.That(largest, Is.EqualTo(1f).Within(1e-3));
        // 4x2x2 scaled uniformly so the 4-unit axis becomes 1 unit: scale factor 0.25, so the other two
        // axes (originally 2) become 0.5.
        Assert.That(size.Y, Is.EqualTo(0.5f).Within(1e-3));
        Assert.That(size.Z, Is.EqualTo(0.5f).Within(1e-3));
    }

    [Test]
    public void NormalizeScene_YawRotatesAroundY()
    {
        // A box longer along X than Z; a 90-degree yaw should swap which axis is long.
        var scene = new Scene { RootNode = new Node("root") };
        var mesh = new Assimp.Mesh("plank", PrimitiveType.Triangle);
        Vector3D[] corners =
        {
            new(-5, -1, -1), new(5, -1, -1), new(5, 1, -1), new(-5, 1, -1),
            new(-5, -1, 1), new(5, -1, 1), new(5, 1, 1), new(-5, 1, 1),
        };
        foreach (var c in corners) mesh.Vertices.Add(c);
        int[][] faces =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
            new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 },
            new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 0, 3, 7 }, new[] { 0, 7, 4 },
        };
        foreach (var f in faces) mesh.Faces.Add(new Face(f));
        mesh.MaterialIndex = 0;
        scene.Meshes.Add(mesh);
        scene.RootNode.MeshIndices.Add(0);
        scene.Materials.Add(new Material { Name = "default" });

        MeshNormalizer.NormalizeScene(scene, new MeshNormalizeOptions { YawDegrees = 90f, TargetLengthUnits = null, Recenter = false });

        var (min, max) = MeshNormalizer.ComputeWorldBounds(scene);
        var size = max - min;
        Assert.That(size.Z, Is.EqualTo(10f).Within(1e-2), "the original 10-unit X extent should now read as Z after a 90-degree yaw");
        Assert.That(size.X, Is.EqualTo(2f).Within(1e-2), "the original 2-unit Z extent should now read as X");
    }

    // ShipModels.cs scales by bounds.size.z specifically, not "whichever axis is largest" -- for an
    // elongated ship those usually coincide, but a faithful drop-in has to support the exact convention,
    // not just the common case. Build a box whose longest axis is X (6 units) but Z is only 3, and confirm
    // NormalizeAxis.Z scales by the 3-unit Z extent alone, ignoring the larger X extent.
    [Test]
    public void NormalizeScene_AxisZ_ScalesByZExtentEvenWhenNotTheLargestDimension()
    {
        var scene = new Scene { RootNode = new Node("root") };
        var mesh = new Assimp.Mesh("hull", PrimitiveType.Triangle);
        Vector3D[] corners =
        {
            new(-3, -1, -1.5f), new(3, -1, -1.5f), new(3, 1, -1.5f), new(-3, 1, -1.5f),
            new(-3, -1, 1.5f), new(3, -1, 1.5f), new(3, 1, 1.5f), new(-3, 1, 1.5f),
        };
        foreach (var c in corners) mesh.Vertices.Add(c);
        int[][] faces =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
            new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 },
            new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 0, 3, 7 }, new[] { 0, 7, 4 },
        };
        foreach (var f in faces) mesh.Faces.Add(new Face(f));
        mesh.MaterialIndex = 0;
        scene.Meshes.Add(mesh);
        scene.RootNode.MeshIndices.Add(0);
        scene.Materials.Add(new Material { Name = "default" });

        MeshNormalizer.NormalizeScene(scene, new MeshNormalizeOptions
        {
            TargetLengthUnits = 1f,
            Axis = NormalizeAxis.Z,
            Recenter = false,
        });

        var (min, max) = MeshNormalizer.ComputeWorldBounds(scene);
        var size = max - min;
        Assert.That(size.Z, Is.EqualTo(1f).Within(1e-3), "Z extent (3 units) should become exactly 1");
        Assert.That(size.X, Is.EqualTo(2f).Within(1e-3), "X extent (6 units) scales by the same factor, landing at 2 -- still the larger axis, which is correct: Axis.Z means scale IS measured by Z, not clamped to make Z the largest");
    }

    // Regression test for a real bug: AssimpNet's Matrix4x4*Matrix4x4 composes leftmost-applied-first
    // ((A*B)*v == B*(A*v)), the opposite of the usual column-vector convention. A flat, single-level scene
    // (root node directly holding the mesh, parentWorld always Identity) can never expose an order bug in
    // the parent/child world-transform chain, since Identity*X == X*Identity regardless of order — this
    // mirrors the real asset-pack FBX structure (root -> a translated child node -> meshes) that first
    // caught it, where a 20-unit child-node offset recentered correctly on X/Z but silently left Y offset
    // by several units.
    [Test]
    public void NormalizeScene_RecentersCorrectly_WithNestedNonIdentityParentTransform()
    {
        var scene = new Scene { RootNode = new Node("root") };
        var child = new Node("child") { Transform = Matrix4x4.FromTranslation(new Vector3D(20, -15, 100)) };
        scene.RootNode.Children.Add(child);

        var mesh = new Assimp.Mesh("hull", PrimitiveType.Triangle);
        Vector3D[] corners =
        {
            new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
            new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
        };
        foreach (var c in corners) mesh.Vertices.Add(c);
        int[][] faces =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 },
            new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 },
            new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 0, 3, 7 }, new[] { 0, 7, 4 },
        };
        foreach (var f in faces) mesh.Faces.Add(new Face(f));
        mesh.MaterialIndex = 0;
        scene.Meshes.Add(mesh);
        child.MeshIndices.Add(0);
        scene.Materials.Add(new Material { Name = "default" });

        // Sanity check: the mesh really is off-origin on all three axes before normalizing.
        var (preMin, preMax) = MeshNormalizer.ComputeWorldBounds(scene);
        var preCenter = (preMin + preMax) * 0.5f;
        Assert.That(preCenter.X, Is.EqualTo(20f).Within(1e-3));
        Assert.That(preCenter.Y, Is.EqualTo(-15f).Within(1e-3));
        Assert.That(preCenter.Z, Is.EqualTo(100f).Within(1e-3));

        MeshNormalizer.NormalizeScene(scene, new MeshNormalizeOptions { TargetLengthUnits = null, Recenter = true });

        var (min, max) = MeshNormalizer.ComputeWorldBounds(scene);
        var center = (min + max) * 0.5f;
        Assert.That(center.X, Is.EqualTo(0f).Within(1e-3));
        Assert.That(center.Y, Is.EqualTo(0f).Within(1e-3));
        Assert.That(center.Z, Is.EqualTo(0f).Within(1e-3));
    }

    [Test]
    public void Normalize_FileRoundTrip_ExportsAndReimportsCorrectly()
    {
        var scene = MakeOffsetCubeScene(new Vector3D(50, 0, 0), halfSize: 3f);

        var tempDir = Path.Combine(Path.GetTempPath(), $"assetforge-mesh-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var inputPath = Path.Combine(tempDir, "source.obj");
        var outputPath = Path.Combine(tempDir, "output.gltf");
        try
        {
            using (var exporter = new AssimpContext())
                Assert.That(exporter.ExportFile(scene, inputPath, "obj"), Is.True);

            // Exercises the real public entry point end to end -- import, normalize, export, and the
            // extension-reconciliation step -- not just the internal scene-mutation logic. This Assimp
            // build's "gltf2" exporter doesn't reliably produce the form implied by the requested
            // extension (sometimes JSON+.bin, sometimes a single binary blob regardless of what's asked
            // for), and its "glb"/"glb2" IDs both silently write legacy glTF 1.0 instead of 2.0 (see
            // MeshNormalizer.Normalize's doc comment) -- so the real output path must come from this
            // method's return value, and must actually reimport, not just exist.
            var actualOutputPath = MeshNormalizer.Normalize(inputPath, outputPath, new MeshNormalizeOptions { TargetLengthUnits = 1f, Recenter = true });
            Assert.That(File.Exists(actualOutputPath), Is.True);

            using var importer = new AssimpContext();
            var reimported = importer.ImportFile(actualOutputPath, PostProcessSteps.Triangulate);
            var (min, max) = MeshNormalizer.ComputeWorldBounds(reimported);
            var size = max - min;
            var largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
            Assert.That(largest, Is.EqualTo(1f).Within(1e-3));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
