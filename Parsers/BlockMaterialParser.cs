using Allumeria.Blocks.Blocks;

internal class BlockMaterialEntry : Dictionary<string, object?>
{
  public string Id => (string)this["id"]!;

  public BlockMaterialEntry(BlockMaterial material, string id)
  {
    this["id"] = id;
    this["miningLevel"] = material.miningLevel;
    this["punchesRequired"] = material.punchesRequired;
    this["overcharge"] = material.overcharge;
    this["swingMultiplier"] = material.swingMultiplier;
    this["canBeBlownUp"] = material.canBeBlownUp;
    this["hammerLevel"] = material.hammerLevel;

    if (material.preferredTool != null)
      this["preferredTool"] = material.preferredTool.strID;
  }
}

internal static class BlockMaterialParser
{
  public static Dictionary<BlockMaterial, BlockMaterialEntry> entries = [];

  public static Dictionary<BlockMaterial, BlockMaterialEntry> Parse()
  {
    var materialMap = Reflection.BuildStaticInstanceNameMap<BlockMaterial>();

    foreach (var (material, id) in materialMap)
      entries[material] = new BlockMaterialEntry(material, id);

    return entries;
  }
}
