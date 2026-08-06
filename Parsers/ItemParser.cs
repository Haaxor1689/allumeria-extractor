using Allumeria.EntitySystem;
using Allumeria.Items;
using Allumeria.Items.ItemTypes;

internal class ItemEntry : Dictionary<string, object?>
{
  private static readonly List<InventorySlot> ValidSlotTypes = new()
  {
    new() { slotType = InventorySlot.SlotType.Helmet },
    new() { slotType = InventorySlot.SlotType.Chestplate },
    new() { slotType = InventorySlot.SlotType.Greaves },
    new() { slotType = InventorySlot.SlotType.Trinket },
    new() { slotType = InventorySlot.SlotType.Ammo },
    new() { slotType = InventorySlot.SlotType.Currency },
  };

  private static readonly HashSet<string> ExcludedSubclassFieldNames = new(
    ["armourType", "armorType", "soundID", "full", "sucksLava", "useSound", "canThrow", "second", "enrage"],
    StringComparer.Ordinal
  );

  public string? Id => this.TryGetValue("id", out var idObj) && idObj is string id ? id : null;

  public string? Sprite => this.TryGetValue("sprite", out var spriteObj) && spriteObj is string sprite ? sprite : Id;

  public string? Model => this.TryGetValue("model", out var modelObj) && modelObj is string model ? model : null;

  public string? Texture =>
    this.TryGetValue("texture", out var textureObj) && textureObj is string texture ? texture : null;

  public string? ProjectileModel =>
    this.TryGetValue("projectileModel", out var modelObj) && modelObj is string model ? model : null;

  public string? ProjectileTexture =>
    this.TryGetValue("projectileTexture", out var textureObj) && textureObj is string texture ? texture : null;

  public ItemEntry(Item item)
  {
    this["id"] = item.strID;

    var className = item.GetType().Name;
    if (className != "Item")
      this["class"] = className;

    if (item.block != null)
      this["block"] = item.block.strID;

    if (item.stackLimit != 512)
      this["stackSize"] = item.stackLimit;

    if (item.sellValue != 0)
      this["sellValue"] = item.sellValue;

    if (item.hideFromBuildMenu)
      this["hidden"] = true;

    if (item.sweeping)
      this["sweeping"] = true;

    if (item.targetsLiquid)
      this["targetLiquid"] = true;

    if (item.swingAnimation != 0)
      this["swingAnim"] = item.swingAnimation;

    if (!string.IsNullOrWhiteSpace(item.itemModelString))
      this["model"] = item.itemModelString;

    if (!string.IsNullOrWhiteSpace(item.itemTextureString))
      this["texture"] = item.itemTextureString;

    if (item.isCurrency)
      this["currencyAmount"] = item.currencyAmount;

    if (item.rarity != 0)
      this["rarity"] = item.rarity;

    if (item.fluid != null && ReflectionHelpers.TryGetStrID(item.fluid) is { } fluidId)
      this["fluid"] = fluidId;

    var tags = BuildTagDictionary(item);
    if (tags.Count > 0)
      this["tags"] = tags;

    AddSubclassFields(item);

    var slotType = ValidSlotTypes.FirstOrDefault(s => s != null && item.AllowedInSlot(s), null)?.slotType.ToString();
    if (!string.IsNullOrWhiteSpace(slotType))
      this["slotType"] = slotType;

    if (!string.IsNullOrWhiteSpace(item.itemSpriteString) && item.itemSpriteString != item.strID)
      this["sprite"] = item.itemSpriteString;

    var categories = ItemCategory
      .categories.Where(category => category != ItemCategory.all && category.items.Contains(item))
      .Select(category => category.strID)
      .ToList();
    if (categories.Count > 0)
      this["category"] = categories;
  }

  private static Dictionary<string, object?> BuildTagDictionary(Item item)
  {
    return item
      .tags.GroupBy(e => e.tagType.strID, StringComparer.OrdinalIgnoreCase)
      .Select(group =>
      {
        var last = group.Last();
        var key = last.tagType.strID;
        object? value = last.hasData ? (object?)last.data : true;
        value = NormalizeTagValue(key, value);
        return new KeyValuePair<string, object?>(key, value);
      })
      .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
      .ToDictionary(pair => pair.Key, pair => pair.Value);
  }

  private static object? NormalizeTagValue(string tagKey, object? value)
  {
    if (!string.Equals(tagKey, "ammo", StringComparison.OrdinalIgnoreCase) || value is null)
      return value;

    if (value is not int ammoIndex)
      return value;

    var ammoTypeNames = ItemAmmo.ammoTypeNames;
    if (ammoIndex < 0 || ammoIndex >= ammoTypeNames.Length)
      return value;

    var ammoTypeName = ammoTypeNames[ammoIndex];
    return string.IsNullOrWhiteSpace(ammoTypeName) ? value : ammoTypeName;
  }

  private void AddSubclassFields(Item item) =>
    ReflectionHelpers.PopulateSubclassFields(
      this,
      item,
      typeof(Item),
      ExcludedSubclassFieldNames,
      (fieldName) =>
        fieldName switch
        {
          "modelName" => "model",
          "textureName" => "texture",
          _ => fieldName,
        },
      (field, obj, dict) =>
      {
        // Special handling to show projectile model
        if (
          field.Name != "type"
          || field.GetValue(obj) is not Type projType
          || !ReflectionHelpers.IsSubtype<Entity>(projType)
        )
          return false;

        var (model, texture) = ReflectionHelpers.GetEntityModelTexture(projType);

        dict.TryGetValue("model", out var existingModel);
        dict.TryGetValue("texture", out var existingTexture);
        if (!string.IsNullOrWhiteSpace(model) && model != existingModel as string)
          dict["projectileModel"] = model;
        if (!string.IsNullOrWhiteSpace(texture) && texture != existingTexture as string)
          dict["projectileTexture"] = texture;
        return !string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(texture);
      }
    );
}

internal static class ItemParser
{
  public static Dictionary<Item, ItemEntry> entries = [];

  public static Dictionary<Item, ItemEntry> Parse()
  {
    Item.AssignPropertiesToBlocks();
    Item.AssignCategories();

    var items = Item
      .items.Where(item => item != null && (item.block is null || item.block.isVariantOf is null))
      .OrderBy(item => item.block is null);

    foreach (var item in items)
      entries[item] = new ItemEntry(item);

    return entries;
  }
}
