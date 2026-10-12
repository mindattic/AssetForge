using Assimp;

namespace MindAttic.AssetForge.Mesh;

/// <summary>
/// Generalizes `UnitySolarSystem`'s `ShipModels.Normalise()` — a runtime fix-up applied the first time a
/// given asset-pack model is loaded (reparent, zero the pack's own layout offset, apply a fixed per-pack
/// yaw, rescale/recenter to a consistent hull length) — into an offline, format-agnostic step: not every
/// project should have to re-derive and bake that fix-up into its own loading code, nor pay its cost every
/// time the game boots.
/// </summary>
/// <summary>Which bounding-box extent <see cref="MeshNormalizeOptions.TargetLengthUnits"/> is measured
/// against.</summary>
public enum NormalizeAxis
{
    /// <summary>Whichever of X/Y/Z is largest.</summary>
    LargestDimension,
    X,
    Y,
    Z,
}

public sealed record MeshNormalizeOptions
{
    /// <summary>Degrees to rotate around the vertical (Y) axis — the fixed per-pack constant an asset
    /// pack's own forward direction needs to match the target convention (e.g. Kenney's packs commonly
    /// need 180°).</summary>
    public float YawDegrees { get; init; } = 0f;

    /// <summary>If set, the model is uniformly rescaled so the <see cref="Axis"/> extent equals this many
    /// units — e.g. 1.0 for "every hull is exactly 1 unit long," <c>ShipModels.cs</c>'s own convention.
    /// Null leaves scale untouched.</summary>
    public float? TargetLengthUnits { get; init; } = 1f;

    /// <summary>Which extent <see cref="TargetLengthUnits"/> measures against. Default
    /// <see cref="NormalizeAxis.LargestDimension"/>; use <see cref="NormalizeAxis.Z"/> to match
    /// <c>ShipModels.cs</c>'s exact convention (hull length along Z specifically, not just whichever axis
    /// happens to be biggest).</summary>
    public NormalizeAxis Axis { get; init; } = NormalizeAxis.LargestDimension;

    /// <summary>Recenters the model's bounding-box center onto the origin (zeroes the pack's own layout
    /// offset) before scaling/rotating.</summary>
    public bool Recenter { get; init; } = true;
}

public static class MeshNormalizer
{
    /// <summary>Imports <paramref name="inputPath"/> (any format Assimp reads — fbx, obj, gltf, ...),
    /// applies the normalize transform, and exports glTF 2.0 under <paramref name="outputPath"/> (its
    /// extension is advisory, not binding — see below). Returns the actual path written, which callers
    /// must use instead of assuming <paramref name="outputPath"/> unchanged. glTF is the target format
    /// projects are standardizing on (open Khronos standard, strict PBR material spec), vs. older
    /// per-pack formats (FBX, ...) with inconsistent material interpretation across exporters.
    ///
    /// Two real bugs/quirks in this Assimp build had to be worked around, both found by converting actual
    /// production ship meshes, not synthetic test scenes:
    /// <list type="bullet">
    /// <item>The "glb"/"glb2" format IDs both silently write the legacy glTF <em>1.0</em> binary layout
    /// regardless of which is requested (confirmed by parsing a real exported file's 12-byte header — the
    /// version field reads back as 1 either way — and reproduced crashing Assimp's own reimporter,
    /// 0xC0000005, on one specific source file). The "gltf2" format ID is the one that actually produces
    /// spec-correct 2.0 content (confirmed: its JSON declares <c>"asset":{"version":"2.0"}</c>), so that's
    /// what this always requests — regardless of what the output extension suggests.</item>
    /// <item>"gltf2" itself doesn't consistently choose JSON+external-.bin vs. single-file binary based on
    /// the requested extension — a trivial single-mesh scene came out as split JSON+.bin even when asked
    /// for a <c>.gltf</c> path, but a real multi-mesh ship came out as a single binary blob (correct glTF 2
    /// binary content, magic+version+JSON/BIN chunks all valid) even though it was ALSO asked for a
    /// <c>.gltf</c> path — and Assimp's own importer picks its parser by extension, so a binary blob
    /// sitting under a <c>.gltf</c> name fails to reimport. Rather than guess which form a given scene
    /// will produce, this reads back the first bytes actually written and renames to <c>.glb</c> (binary
    /// form; deletes a stray <c>.bin</c>/<c>.gltf</c> sibling if present) or leaves a <c>.gltf</c>+<c>.bin</c>
    /// pair alone (JSON form), so the file on disk is always self-consistent with its own content,
    /// whichever way the exporter decided to go.</item>
    /// </list>
    /// Unity's glTFast importer (and glTF tooling generally) reads both forms identically.</summary>
    public static string Normalize(string inputPath, string outputPath, MeshNormalizeOptions options)
    {
        using var importer = new AssimpContext();
        var scene = importer.ImportFile(inputPath, PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices);

        NormalizeScene(scene, options);

        using var exporter = new AssimpContext();
        if (!exporter.ExportFile(scene, outputPath, "gltf2"))
            throw new InvalidOperationException($"AssimpNet failed to export '{outputPath}'");

        return ReconcileExtensionWithActualContent(outputPath);
    }

    /// <summary>Detects whether what got written to <paramref name="path"/> is binary glTF (starts with
    /// the "glTF" magic) or plain JSON, and renames it to the matching extension (<c>.glb</c> or
    /// <c>.gltf</c>) if it doesn't already have one — see <see cref="Normalize"/>'s doc comment for why
    /// this is necessary rather than assumed. Returns the final path.</summary>
    private static string ReconcileExtensionWithActualContent(string path)
    {
        Span<byte> magic = stackalloc byte[4];
        using (var stream = File.OpenRead(path))
        {
            if (stream.Read(magic) != 4) return path;
        }
        var isBinary = magic is [(byte)'g', (byte)'l', (byte)'T', (byte)'F'];
        var wantExtension = isBinary ? ".glb" : ".gltf";
        if (string.Equals(Path.GetExtension(path), wantExtension, StringComparison.OrdinalIgnoreCase))
            return path;

        var correctedPath = Path.ChangeExtension(path, wantExtension);
        File.Move(path, correctedPath, overwrite: true);

        if (isBinary)
        {
            // A binary export carries everything in the one file; a stray .bin this same base name might
            // already have from a previous JSON-form export would be stale leftovers, not part of this one.
            var staleBin = Path.ChangeExtension(path, ".bin");
            if (File.Exists(staleBin)) File.Delete(staleBin);
        }
        return correctedPath;
    }

    /// <summary>Applies the normalize transform, baked directly into every mesh's vertex data (not left
    /// as a root-node transform). A format exporter is free to special-case or silently reinterpret a
    /// non-identity root/node transform (axis-convention bakes are exactly the kind of per-format quirk
    /// this tool exists to paper over) — baking into vertices and flattening every node's own transform
    /// to identity leaves nothing for an exporter to reinterpret: what you see in-memory after this call
    /// is exactly what a round-trip export/reimport will show, regardless of hierarchy depth or the
    /// source/target format's own axis conventions. Exposed separately from <see cref="Normalize"/> so it
    /// can be tested against an in-memory scene without needing real mesh files on disk.</summary>
    internal static void NormalizeScene(Scene scene, MeshNormalizeOptions options)
    {
        var (min, max) = ComputeWorldBounds(scene);
        var center = (min + max) * 0.5f;
        var size = max - min;
        var measuredDimension = options.Axis switch
        {
            NormalizeAxis.X => size.X,
            NormalizeAxis.Y => size.Y,
            NormalizeAxis.Z => size.Z,
            _ => Math.Max(size.X, Math.Max(size.Y, size.Z)),
        };

        var translate = options.Recenter ? Matrix4x4.FromTranslation(-center) : Matrix4x4.Identity;
        var scale = (options.TargetLengthUnits is { } target && measuredDimension > 1e-6f)
            ? Matrix4x4.FromScaling(new Vector3D(target / measuredDimension, target / measuredDimension, target / measuredDimension))
            : Matrix4x4.Identity;
        var rotate = Math.Abs(options.YawDegrees) > 1e-6f
            ? Matrix4x4.FromRotationY(options.YawDegrees * (float)(Math.PI / 180.0))
            : Matrix4x4.Identity;

        // AssimpNet composes Matrix4x4*Matrix4x4 leftmost-applied-first: (A*B)*v == B*(A*v), the OPPOSITE
        // of the usual column-vector convention (confirmed empirically, not assumed — see MeshNormalizerTests
        // for the regression coverage that originally caught this the hard way, against a real multi-node
        // FBX where a single flat test scene couldn't expose it). Desired order is translate (recenter
        // around the ORIGINAL bounds) first, then scale, then rotate — so build it translate*scale*rotate.
        var normalizeTransform = translate * scale * rotate;

        BakeWorldTransformsIntoVertices(scene, normalizeTransform);
    }

    /// <summary>Walks the node hierarchy, transforms every mesh's vertices (and normals, if present) by
    /// <paramref name="extra"/> composed with that mesh's own resolved world transform, then sets every
    /// node's own transform to identity. A mesh referenced by more than one node (instancing) is
    /// duplicated per extra instance first, so each copy can be baked with its own distinct world
    /// transform without the instances fighting over one shared vertex buffer.</summary>
    private static void BakeWorldTransformsIntoVertices(Scene scene, Matrix4x4 extra)
    {
        var referencingNodeCount = new int[scene.MeshCount];
        void CountRefs(Node node)
        {
            foreach (var mi in node.MeshIndices) referencingNodeCount[mi]++;
            foreach (var child in node.Children) CountRefs(child);
        }
        CountRefs(scene.RootNode);

        void Visit(Node node, Matrix4x4 parentWorld)
        {
            // Leftmost-applied-first (see note in NormalizeScene): node.Transform (local-to-parent) must
            // be written first, then parentWorld (parent-to-root-world) after it.
            var world = node.Transform * parentWorld;
            var combined = world * extra;
            // Normals are directions, not points: strip translation before applying to them. Exact for
            // rotation + uniform scale (renormalized after) — the only transforms this tool ever builds;
            // a general inverse-transpose isn't needed since there's no shear/non-uniform scale in play.
            var normalMatrix = combined;
            normalMatrix.A4 = 0f;
            normalMatrix.B4 = 0f;
            normalMatrix.C4 = 0f;

            for (var i = 0; i < node.MeshIndices.Count; i++)
            {
                var meshIndex = node.MeshIndices[i];
                var mesh = scene.Meshes[meshIndex];
                if (referencingNodeCount[meshIndex] > 1)
                {
                    // Shared mesh: this node gets its own private copy so baking its transform doesn't
                    // also move every other node that references the original.
                    var copy = CloneMesh(mesh);
                    scene.Meshes.Add(copy);
                    node.MeshIndices[i] = scene.Meshes.Count - 1;
                    mesh = copy;
                }

                for (var v = 0; v < mesh.VertexCount; v++)
                    mesh.Vertices[v] = combined * mesh.Vertices[v];

                if (mesh.HasNormals)
                    for (var n = 0; n < mesh.Normals.Count; n++)
                        mesh.Normals[n] = TransformNormal(normalMatrix, mesh.Normals[n]);
            }

            node.Transform = Matrix4x4.Identity;
            foreach (var child in node.Children) Visit(child, world);
        }

        Visit(scene.RootNode, Matrix4x4.Identity);
        scene.RootNode.Transform = Matrix4x4.Identity;
    }

    private static Assimp.Mesh CloneMesh(Assimp.Mesh source)
    {
        var copy = new Assimp.Mesh(source.Name, source.PrimitiveType) { MaterialIndex = source.MaterialIndex };
        copy.Vertices.AddRange(source.Vertices);
        if (source.HasNormals) copy.Normals.AddRange(source.Normals);
        foreach (var face in source.Faces) copy.Faces.Add(new Face(face.Indices.ToArray()));
        return copy;
    }

    private static Vector3D TransformNormal(Matrix4x4 inverseTranspose, Vector3D normal)
    {
        var transformed = inverseTranspose * normal;
        var length = (float)Math.Sqrt(transformed.X * transformed.X + transformed.Y * transformed.Y + transformed.Z * transformed.Z);
        return length > 1e-8f ? transformed / length : transformed;
    }

    internal static (Vector3D min, Vector3D max) ComputeWorldBounds(Scene scene)
    {
        var min = new Vector3D(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3D(float.MinValue, float.MinValue, float.MinValue);

        void Visit(Node node, Matrix4x4 parentWorld)
        {
            var world = node.Transform * parentWorld; // leftmost-applied-first, see NormalizeScene's note
            foreach (var meshIndex in node.MeshIndices)
            {
                foreach (var v in scene.Meshes[meshIndex].Vertices)
                {
                    var wv = world * v;
                    min = new Vector3D(Math.Min(min.X, wv.X), Math.Min(min.Y, wv.Y), Math.Min(min.Z, wv.Z));
                    max = new Vector3D(Math.Max(max.X, wv.X), Math.Max(max.Y, wv.Y), Math.Max(max.Z, wv.Z));
                }
            }
            foreach (var child in node.Children) Visit(child, world);
        }

        Visit(scene.RootNode, Matrix4x4.Identity);
        return (min, max);
    }
}
