using Allumeria.Blocks.BlockModels;
using Allumeria.EntitySystem;
using OpenTK.Mathematics;

internal class CuboidMeshData : Dictionary<string, object?>
{
  public CuboidMeshData(
    Cuboid cuboid,
    Matrix4 matrix = default,
    int boundsX = 16,
    int boundsY = 16,
    int boundsZ = 16,
    bool inverted = false
  )
  {
    this["from"] = new[] { (int)cuboid.minX, cuboid.minY, cuboid.minZ };
    this["to"] = new[] { (int)cuboid.maxX, cuboid.maxY, cuboid.maxZ };

    // byte[] would be serialized as base64 rather than face indices.
    if (cuboid.textureIndices.Any(index => index != 0))
      this["textureIndices"] = cuboid.textureIndices.Select(index => (int)index).ToArray();
    if (cuboid.useOffsets)
      this["uvOffsets"] = cuboid.uvOffsets.Select(offset => new[] { offset.X, offset.Y }).ToArray();
    if (!cuboid.useAO)
      this["useAO"] = false;
    if (cuboid.flag != 0)
      this["flag"] = cuboid.flag;
    if (cuboid.ignorePaint)
      this["ignorePaint"] = true;
    if (cuboid.noNormals)
      this["noNormals"] = true;
    if (inverted)
      this["inverted"] = true;
    if (boundsX != 16 || boundsY != 16 || boundsZ != 16)
      this["uvBounds"] = new[] { boundsX, boundsY, boundsZ };
    if (matrix != default)
      this["transform"] = new[]
      {
        new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M14 },
        new[] { matrix.M21, matrix.M22, matrix.M23, matrix.M24 },
        new[] { matrix.M31, matrix.M32, matrix.M33, matrix.M34 },
        new[] { matrix.M41, matrix.M42, matrix.M43, matrix.M44 },
      };
  }
}

internal class QuadMeshData : Dictionary<string, object?>
{
  public int Flag => TryGetValue("flag", out var val) && val is int s ? s : 0;

  public QuadMeshData(BlockQuad quad)
  {
    this["vertices"] = quad.positions.Select(v => new[] { v.X * 16, v.Y * 16, v.Z * 16 }).ToArray();

    if (quad.useTextureIndex && quad.textureIndex != 0)
      this["textureIndex"] = quad.textureIndex;

    var uvs = quad.uvs;
    if (!IsDefaultQuadUvs(uvs))
      this["uvs"] = new[] { uvs.umin, uvs.vmin, uvs.umin + uvs.umax, uvs.vmin + uvs.vmax };

    if (quad.flag != 0)
      this["flag"] = quad.flag;
  }

  private static bool IsDefaultQuadUvs(FaceUV uv) => uv.umin == 0 && uv.vmin == 0 && uv.umax == 16 && uv.vmax == 16;
}

internal class ColliderData : Dictionary<string, object?>
{
  public ColliderData(Collider collider)
  {
    var to = collider.position + collider.size;
    this["from"] = new[] { collider.position.X * 16, collider.position.Y * 16, collider.position.Z * 16 };
    this["to"] = new[] { to.X * 16, to.Y * 16, to.Z * 16 };
    this["type"] = collider.colliderType.ToString();
  }
}

internal class BlockModelEntry : Dictionary<string, object?>
{
  public string Id => TryGetValue("id", out var val) && val is string s ? s : "";

  public BlockModelEntry(
    string id,
    IEnumerable<CuboidMeshData> cuboids,
    IEnumerable<BlockQuad> quads,
    IEnumerable<Collider> colliders
  )
  {
    this["id"] = id;
    this["cuboids"] = cuboids.ToArray();
    var colliderData = colliders.Select(collider => new ColliderData(collider)).ToArray();
    if (colliderData.Length > 0)
      this["colliders"] = colliderData;
    var quadData = quads.Select(quad => new QuadMeshData(quad)).OrderBy(quad => quad.Flag).ToArray();
    if (quadData.Length > 0)
      this["quads"] = quadData;
  }
}

internal static class BlockModelParser
{
  public static Dictionary<BlockModel, BlockModelEntry> entries = new();

  public static Dictionary<BlockModel, BlockModelEntry> Parse()
  {
    var quadsMap = Reflection.BuildStaticInstanceNameMap<BlockModelQuads>();

    entries[new BlockModel()] = new BlockModelEntry(
      "cube",
      [new CuboidMeshData(new Cuboid(0, 0, 0, 16, 16, 16))],
      [],
      []
    );

    var capturedModels = BlockModelCapture.Read();

    foreach (var (model, id) in quadsMap)
    {
      if (!capturedModels.TryGetValue(id, out var captured))
        throw new InvalidOperationException($"Could not capture block model '{id}'.");

      entries[model] = new BlockModelEntry(
        id,
        captured.Cuboids,
        model.quads.Where((_, index) => !captured.CuboidQuadIndices.Contains(index)),
        model.colliders
      );
    }

    // Implement topBottom using BlockModelQuads instead of custom cuboid
    entries[BlockModelTopBottom.topBottom] = new BlockModelEntry(
      "topBottom",
      [new CuboidMeshData(new Cuboid(0, 0, 0, 16, 16, 16, textureIndices: [0, 2, 1, 1, 1, 1]))],
      [],
      []
    );

    // Share the captured six-sided model with its custom-model alias.
    entries[BlockModelSixSided.model] = entries[BlockModelQuads.sixSided];

    return entries;
  }

  public static void Cleanup() => entries.Remove(BlockModelSixSided.model);
}
