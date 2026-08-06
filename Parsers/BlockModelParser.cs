using Allumeria.Blocks.BlockModels;
using Microsoft.CodeAnalysis;

internal class QuadMeshData : Dictionary<string, object?>
{
  public int Flag => TryGetValue("flag", out var val) && val is int s ? s : 0;

  public QuadMeshData(BlockQuad quad)
  {
    this["vertices"] = quad.positions.Select(v => new[] { v.X, v.Y, v.Z }).ToArray();

    if (quad.useTextureIndex && quad.textureIndex != 0)
      this["textureIndex"] = quad.textureIndex;

    var uvs = quad.uvs;
    if (!IsDefaultQuadUvs(quad.uvs))
      this["uvs"] = new[] { uvs.umin, uvs.vmin, uvs.umin + uvs.umax, uvs.vmin + uvs.vmax };

    if (quad.flag != 0)
      this["flag"] = quad.flag;
  }

  private static bool IsDefaultQuadUvs(FaceUV uv) => uv.umin == 0 && uv.vmin == 0 && uv.umax == 16 && uv.vmax == 16;
}

internal class BlockModelEntry : Dictionary<string, object?>
{
  public string Id => TryGetValue("id", out var val) && val is string s ? s : "";

  public BlockModelEntry(string id, BlockModelQuads model)
  {
    this["id"] = id;
    this["meshes"] = model
      .quads.Select(quad => new QuadMeshData(quad))
      .OrderBy(quad => quad.Flag)
      .Cast<object>()
      .ToList();
  }
}

internal static class BlockModelParser
{
  public static Dictionary<BlockModel, BlockModelEntry> entries = new();

  public static Dictionary<BlockModel, BlockModelEntry> Parse()
  {
    var quadsMap = ReflectionHelpers.BuildStaticInstanceNameMap<BlockModelQuads>();

    entries[new BlockModel()] = new BlockModelEntry(
      "cube",
      new BlockModelQuads().AddCuboid(new Cuboid(0, 0, 0, 16, 16, 16))
    );

    foreach (var (model, id) in quadsMap)
      entries[model] = new BlockModelEntry(id, model);

    // Implement topBottom using BlockModelQuads instead of custom cuboid
    entries[BlockModelTopBottom.topBottom] = new BlockModelEntry(
      "topBottom",
      new BlockModelQuads().AddCuboid(new Cuboid(0, 0, 0, 16, 16, 16, textureIndices: [0, 2, 1, 1, 1, 1]))
    );

    // TODO: Rename to sixSided after block parser is updated
    entries[BlockModelSixSided.model] = new BlockModelEntry(
      "model",
      new BlockModelQuads().AddCuboid(new Cuboid(0, 0, 0, 16, 16, 16, textureIndices: [0, 1, 2, 3, 4, 5]))
    );

    return entries;
  }
}
