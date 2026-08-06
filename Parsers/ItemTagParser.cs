using Allumeria.Items.ItemTagTypes;

internal class ItemTagEntry : Dictionary<string, object?>
{
  public int? IconX => TryGetValue("iconX", out var val) && val is int i ? i : null;

  public int? IconY => TryGetValue("iconY", out var val) && val is int i ? i : null;

  public ItemTagEntry(ItemTag tag)
  {
    this["id"] = tag.strID;

    var className = tag.GetType().Name;
    if (className != "ItemTag")
      this["class"] = className;

    if (tag.hasIcon)
    {
      this["iconX"] = tag.iconX;
      this["iconY"] = tag.iconY;
    }
  }
}

internal static class ItemTagParser
{
  public static Dictionary<ItemTag, ItemTagEntry> entries = [];

  public static Dictionary<ItemTag, ItemTagEntry> Parse()
  {
    var itemTags = ItemTag.tags.Where(tag => tag != null);

    foreach (var tag in itemTags)
      entries[tag] = new ItemTagEntry(tag);

    return entries;
  }
}
