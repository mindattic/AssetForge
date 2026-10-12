using System.Numerics;
using SharpGLTF.Schema2;

namespace MindAttic.AssetForge.Mesh;

/// <summary>
/// glTF-native normalize path, used instead of <see cref="MeshNormalizer"/>'s AssimpNet pipeline whenever
/// the input is already glTF (.gltf/.glb). Found necessary converting UnityEve's skinned/animated
/// creature models: AssimpNet's import/export round-trip corrupts animation data for rigged meshes
/// (confirmed on Spider.glb — 5 real animation clips became 195 after a round-trip through Assimp's glTF
/// exporter, most likely one spurious clip per bone-channel instead of one clip with many channels; 39
/// bones x 5 clips is suspiciously exactly 195). SharpGLTF reads and writes glTF natively (no intermediate
/// generic scene-graph translation to lose fidelity in) and round-trips animations/skins exactly,
/// confirmed on the same file.
///
/// Also simpler than <see cref="MeshNormalizer"/>'s vertex-baking approach: rather than baking the extra
/// transform into every vertex and flattening node transforms to identity, this applies it once to each
/// top-level scene node's own local matrix. That's correct for a skinned mesh too — the whole skeleton
/// (and its animations, defined relative to each joint's own local space) inherits a rigid transform
/// applied above the whole hierarchy without needing to touch a single bone or keyframe.
/// </summary>
public static class GltfMeshNormalizer
{
    public static string Normalize(string inputPath, string outputPath, MeshNormalizeOptions options)
    {
        var model = ModelRoot.Load(inputPath);

        var (min, max) = ComputeWorldBounds(model);
        var center = (min + max) * 0.5f;
        var size = max - min;
        var measuredDimension = options.Axis switch
        {
            NormalizeAxis.X => size.X,
            NormalizeAxis.Y => size.Y,
            NormalizeAxis.Z => size.Z,
            _ => Math.Max(size.X, Math.Max(size.Y, size.Z)),
        };

        var translate = options.Recenter ? Matrix4x4.CreateTranslation(-center) : Matrix4x4.Identity;
        var scale = (options.TargetLengthUnits is { } target && measuredDimension > 1e-6f)
            ? Matrix4x4.CreateScale(target / measuredDimension)
            : Matrix4x4.Identity;
        var rotate = Math.Abs(options.YawDegrees) > 1e-6f
            ? Matrix4x4.CreateRotationY(options.YawDegrees * (float)(Math.PI / 180.0))
            : Matrix4x4.Identity;

        // System.Numerics.Matrix4x4 composes leftmost-applied-first too (confirmed empirically, same as
        // AssimpNet's convention, not assumed) -- translate first, then scale, then rotate.
        var extra = translate * scale * rotate;

        if (extra != Matrix4x4.Identity)
        {
            foreach (var root in model.DefaultScene.VisualChildren.ToList())
                root.LocalMatrix = extra * root.LocalMatrix;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (string.Equals(Path.GetExtension(outputPath), ".glb", StringComparison.OrdinalIgnoreCase))
            model.SaveGLB(outputPath);
        else
            model.SaveGLTF(outputPath);

        return outputPath;
    }

    public static bool IsGltfPath(string path) =>
        path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

    private static (Vector3 min, Vector3 max) ComputeWorldBounds(ModelRoot model)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        void Visit(Node node)
        {
            if (node.Mesh != null)
            {
                var world = node.WorldMatrix;
                foreach (var primitive in node.Mesh.Primitives)
                {
                    var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                    if (positions is null) continue;
                    foreach (var p in positions)
                    {
                        var wp = Vector3.Transform(p, world);
                        min = Vector3.Min(min, wp);
                        max = Vector3.Max(max, wp);
                    }
                }
            }
            foreach (var child in node.VisualChildren) Visit(child);
        }

        foreach (var root in model.DefaultScene.VisualChildren) Visit(root);
        return (min, max);
    }
}
