using System.Reflection;
using Allumeria.Blocks.Blocks;
using Allumeria.Items;
using Allumeria.Items.ItemTagTypes;
using Allumeria.Items.LootTables;

internal class LootDescriptionEntry : Dictionary<string, object?>
{
  public string? Id => TryGetValue("id", out var val) ? (string?)val : null;

  public LootDescriptionEntry(LootDescription loot)
  {
    Reflection.GetPrivate<string>(loot, "strID", out var lootId);
    this["id"] = lootId;

    this["group"] = loot.group.ToString();

    if (Reflection.GetPrivate<IEnumerable<LootEntry>>(loot, "entries", out var entries))
    {
      if (entries.Count() == 1)
      {
        var dict = ResolveLootEntry(entries.First());
        foreach (var kvp in dict)
          this[kvp.Key] = kvp.Value;
      }
      else
      {
        this["entries"] = entries.Select(ResolveLootEntry).ToList();
      }
    }
  }

  private static Dictionary<string, object?> ResolveLootEntry(LootEntry entry)
  {
    var dict = new Dictionary<string, object?>();

    if (entry.childEntries.Count == 1)
      dict = ResolveLootEntry(entry.childEntries[0]);

    var type = entry.GetType();

    switch (entry)
    {
      case LootChance chance:
      {
        var chanceField = type.GetField("chance", BindingFlags.NonPublic | BindingFlags.Instance);
        if (chanceField?.GetValue(chance) is float chanceValue)
          dict["chance"] = chanceValue;

        break;
      }
      case LootChooseExclusive choose:
      {
        dict["oneOf"] = true;
        break;
      }
      case LootFixedItem fixedItem:
      {
        if (Reflection.GetPrivate<Item>(fixedItem, "item", out var itemFixed))
          dict["item"] = itemFixed.strID;

        if (Reflection.GetPrivate<int>(fixedItem, "amount", out var amount))
          dict["amount"] = amount;

        break;
      }
      case LootPerPlayer lootPerPlayer:
      {
        dict["perPlayer"] = true;
        break;
      }
      case LootRandomAmount lootRandomAmount:
      {
        if (Reflection.GetPrivate<Item>(lootRandomAmount, "item", out var item))
          dict["item"] = item.strID;

        if (Reflection.GetPrivate<int>(lootRandomAmount, "min", out var min))
          dict["min"] = min;

        if (Reflection.GetPrivate<int>(lootRandomAmount, "max", out var max))
          dict["max"] = max;

        break;
      }
      case LootRequireItemTag lootRequireItemTag:
      {
        if (Reflection.GetPrivate<ItemTag>(lootRequireItemTag, "tag", out var tag))
          dict["needs"] = tag.strID;

        break;
      }
    }

    if (entry.childEntries.Count > 1)
      dict["entries"] = entry.childEntries.Select(e => ResolveLootEntry(e)).ToList();

    return dict;
  }
}

internal static class LootParser
{
  public static Dictionary<LootDescription, LootDescriptionEntry> entries = [];

  public static Dictionary<LootDescription, LootDescriptionEntry> Parse()
  {
    // Manually add the dirt entry
    new LootDescription("dirt", LootDescription.LootGroup.Misc).AddEntry(new LootFixedItem(Block.dirt.item, 1));

    foreach (var loot in LootDescription.registeredEntries)
      entries[loot] = new LootDescriptionEntry(loot);

    return entries;
  }
}
