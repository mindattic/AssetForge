using System.Numerics;
using MindAttic.AssetForge.Mesh;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace MindAttic.AssetForge.Tests;

public class GltfMeshNormalizerTests
{
    // A 2x2x2 cube, offset from the origin, with one animated translation track on its node -- stands in
    // for a rigged creature model (minus the actual skin/joints, which SharpGLTF.Toolkit's builder API
    // doesn't make trivial to construct by hand; the animation-preservation risk this guards against is
    // in the animation *data*, not specifically skinning, so a plain animated node exercises it).
    private static string BuildAnimatedCubeFixture(string path, Vector3 offset)
    {
        var material = new MaterialBuilder("default");
        var mesh = VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>.CreateCompatibleMesh("cube");
        var prim = mesh.UsePrimitive(material);
        Vector3[] c =
        {
            new Vector3(-1, -1, -1) + offset, new Vector3(1, -1, -1) + offset, new Vector3(1, 1, -1) + offset, new Vector3(-1, 1, -1) + offset,
            new Vector3(-1, -1, 1) + offset, new Vector3(1, -1, 1) + offset, new Vector3(1, 1, 1) + offset, new Vector3(-1, 1, 1) + offset,
        };
        void Quad(int a, int b, int cc, int d)
        {
            prim.AddTriangle(new VertexPosition(c[a]), new VertexPosition(c[b]), new VertexPosition(c[cc]));
            prim.AddTriangle(new VertexPosition(c[a]), new VertexPosition(c[cc]), new VertexPosition(c[d]));
        }
        Quad(0, 1, 2, 3); Quad(4, 5, 6, 7); Quad(0, 1, 5, 4); Quad(2, 3, 7, 6); Quad(1, 2, 6, 5); Quad(0, 3, 7, 4);

        var node = new NodeBuilder("root");
        node.UseTranslation("bounce").WithPoint(0, Vector3.Zero).WithPoint(1, new Vector3(0, 2, 0));

        var scene = new SceneBuilder();
        scene.AddRigidMesh(mesh, node);
        var model = scene.ToGltf2();
        model.SaveGLB(path);
        return path;
    }

    [Test]
    public void Normalize_PreservesAnimations_WhenNoTransformRequested()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"assetforge-gltf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = BuildAnimatedCubeFixture(Path.Combine(dir, "in.glb"), new Vector3(50, 0, 0));
            var outputPath = Path.Combine(dir, "out.glb");

            var actualPath = MeshNormalizer.Normalize(input, outputPath, new MeshNormalizeOptions
            {
                Recenter = false,
                TargetLengthUnits = null,
                YawDegrees = 0f,
            });

            var result = ModelRoot.Load(actualPath);
            Assert.That(result.LogicalAnimations.Count, Is.EqualTo(1), "the animation must survive a no-op normalize pass");
            Assert.That(result.LogicalAnimations[0].Name, Is.EqualTo("bounce"));
            Assert.That(result.LogicalAnimations[0].Duration, Is.EqualTo(1f).Within(1e-3));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Normalize_PreservesAnimations_WhenRecenteringAndRescaling()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"assetforge-gltf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = BuildAnimatedCubeFixture(Path.Combine(dir, "in.glb"), new Vector3(50, 0, 0));
            var outputPath = Path.Combine(dir, "out.glb");

            var actualPath = MeshNormalizer.Normalize(input, outputPath, new MeshNormalizeOptions
            {
                Recenter = true,
                TargetLengthUnits = 1f,
                YawDegrees = 90f,
            });

            var result = ModelRoot.Load(actualPath);
            Assert.That(result.LogicalAnimations.Count, Is.EqualTo(1), "the animation must survive a real recenter/rescale/yaw too");

            // Geometry: the cube's own bbox (2x2x2, centered at the offset) should now be centered on
            // the origin and rescaled so its largest dimension is 1.
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var node in result.DefaultScene.VisualChildren)
            {
                if (node.Mesh is null) continue;
                var world = node.WorldMatrix;
                foreach (var prim in node.Mesh.Primitives)
                {
                    foreach (var p in prim.GetVertexAccessor("POSITION").AsVector3Array())
                    {
                        var wp = Vector3.Transform(p, world);
                        min = Vector3.Min(min, wp);
                        max = Vector3.Max(max, wp);
                    }
                }
            }
            var center = (min + max) * 0.5f;
            Assert.That(center.X, Is.EqualTo(0f).Within(1e-3));
            Assert.That(center.Y, Is.EqualTo(0f).Within(1e-3));
            Assert.That(center.Z, Is.EqualTo(0f).Within(1e-3));
            var size = max - min;
            var largest = Math.Max(size.X, Math.Max(size.Y, size.Z));
            Assert.That(largest, Is.EqualTo(1f).Within(1e-3));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void IsGltfPath_RecognizesGltfAndGlbCaseInsensitively()
    {
        Assert.That(GltfMeshNormalizer.IsGltfPath("model.glb"), Is.True);
        Assert.That(GltfMeshNormalizer.IsGltfPath("model.GLB"), Is.True);
        Assert.That(GltfMeshNormalizer.IsGltfPath("model.gltf"), Is.True);
        Assert.That(GltfMeshNormalizer.IsGltfPath("model.fbx"), Is.False);
    }
}
