using Allumeria.Blocks.Blocks;
using Allumeria.Items;
using Allumeria.Items.Crafting;

internal class BlockEntry : Dictionary<string, object?>
{
  public string Id => TryGetValue("id", out var val) && val is string s ? s : "";

  public string Class => TryGetValue("class", out var val) && val is string s ? s : "";

  public string[]? Textures => TryGetValue("textures", out var val) ? (string[]?)val : null;

  public BlockEntry(Block block)
  {
    this["id"] = block.strID;

    var className = block.GetType().Name;
    if (className != "Block")
      this["class"] = className[5..];

    BlockMaterialParser.entries.TryGetValue(block.blockMaterial, out var blockMaterialEntry);
    if (blockMaterialEntry != null)
      this["material"] = blockMaterialEntry.Id;

    if (block.spawnEntry != null)
    {
      SpawnParser.entries.TryGetValue(block.spawnEntry, out var spawnEntry);
      if (spawnEntry != null)
        this["spawn"] = spawnEntry.Id;
    }

    if (Reflection.GetPrivate<bool>(block, "interactible", out var interactible) && interactible)
      this["interactible"] = true;

    if (block.slabVariant != null)
      this["canBeShaped"] = true;

    if (block.canBeFelled)
      this["canBeFelled"] = true;

    if (block.hideFromBuildMenu)
      this["hidden"] = true;

    if (block.needsSupport)
      this["needsSupport"] = true;

    // TODO: Fix with real solution after early access release
    if (block.strID == "alpha_shop")
      this["catalogue"] = "shop_alpha";

    if (block.textureStrings.Length > 0)
      this["textures"] = block
        .textureStrings.Select(t =>
        {
          if ((Class == "ToggleLamp" && Id != "logic_pixel") || Class == "Pumpkin")
          {
            if (t.EndsWith("_off"))
              return null;
            if (t.EndsWith("_on"))
              return t + "off";
          }
          else if (Id.StartsWith("lamp_"))
          {
            return t + "_both";
          }
          return t;
        })
        .Where(t => t != null)
        .ToArray();

    if (block is BlockCraftingStation blockCraftingStation)
      this["craftingStation"] = blockCraftingStation.craftingStation.strID;

    BlockModelParser.entries.TryGetValue(block.model, out var blockModelEntry);
    if (blockModelEntry != null)
      this["blockModel"] = blockModelEntry.Id;

    if (block.decorationScore != 0)
      this["decorationScore"] = block.decorationScore;

    if (block.standOnEffect != null)
    {
      EffectParser.entries.TryGetValue(block.standOnEffect, out var standOnEffectEntry);
      if (standOnEffectEntry != null)
        this["standOnEffect"] = standOnEffectEntry.Id;
    }

    if (block.customLoot != null)
    {
      LootParser.entries.TryGetValue(block.customLoot, out var lootEntry);
      if (lootEntry != null)
        this["loot"] = lootEntry.Id;
    }
    else if (block.dropItem.strID != block.strID)
    {
      var lootEntry = LootParser.entries.Values.FirstOrDefault(entry => entry.Id == block.dropItem.strID);
      if (lootEntry != null)
        this["loot"] = lootEntry.Id;
    }

    if (block.emitsLight && Reflection.GetPrivate<byte[]>(block, "lightEmission", out var lightEmission))
      this["lightEmission"] = lightEmission.Select(b => (int)b).ToArray()[0..3];

    if (block.item.strID != block.strID)
      this["item"] = block.item.strID;

    if (block is BlockDoor)
    {
      if (Reflection.GetPrivate<Item>(block, "keyItem", out var keyItem))
        this["keyItem"] = keyItem.strID;
    }

    if (block is BlockCrop blockCrop)
    {
      if (blockCrop.spreadsSelf)
        this["spreadsSelf"] = true;

      if (blockCrop.isMutated)
        this["isMutated"] = true;

      if (blockCrop.harvestLoot != null)
      {
        LootParser.entries.TryGetValue(blockCrop.harvestLoot, out var lootEntry);
        if (lootEntry != null)
          this["harvestLoot"] = lootEntry.Id;
      }
    }
  }
}

internal static class BlockParser
{
  public static Dictionary<Block, BlockEntry> entries = [];

  public static Dictionary<Block, BlockEntry> Parse()
  {
    var blocks = Block
      .blocks.Where(block => block != null && (block.isVariantOf is null))
      .OrderBy(block => block.isVariantOf is null);

    foreach (var block in blocks)
      entries[block] = new BlockEntry(block);

    return entries;
  }
}
