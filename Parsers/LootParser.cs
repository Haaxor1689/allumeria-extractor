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
    var striIdField = typeof(LootDescription).GetField(
      "strID",
      BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
    );
    this["id"] = striIdField?.GetValue(loot);

    this["group"] = loot.group.ToString();

    var entriesType = typeof(LootDescription).GetField(
      "entries",
      BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
    );
    if (entriesType?.GetValue(loot) is IEnumerable<LootEntry> entries)
      if (entries.Count() == 1)
      {
        var dict = ResolveLootEntry(entries.First());
        foreach (var kvp in dict)
          this[kvp.Key] = kvp.Value;
      }
      else
      {
        this["entries"] = entries.Select(e => ResolveLootEntry(e)).ToList();
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
        var itemField = type.GetField("item", BindingFlags.NonPublic | BindingFlags.Instance);
        if (itemField?.GetValue(fixedItem) is Item itemFixed)
          dict["item"] = itemFixed.strID;

        var amountField = type.GetField("amount", BindingFlags.NonPublic | BindingFlags.Instance);
        if (amountField?.GetValue(fixedItem) is int amount)
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
        var entryField = type.GetField("item", BindingFlags.NonPublic | BindingFlags.Instance);
        if (entryField?.GetValue(lootRandomAmount) is Item item)
          dict["item"] = item.strID;

        var minField = type.GetField("min", BindingFlags.NonPublic | BindingFlags.Instance);
        if (minField?.GetValue(lootRandomAmount) is int min)
          dict["min"] = min;

        var maxField = type.GetField("max", BindingFlags.NonPublic | BindingFlags.Instance);
        if (maxField?.GetValue(lootRandomAmount) is int max)
          dict["max"] = max;

        break;
      }
      case LootRequireItemTag lootRequireItemTag:
      {
        var tagField = type.GetField("tag", BindingFlags.NonPublic | BindingFlags.Instance);
        if (tagField?.GetValue(lootRequireItemTag) is ItemTag tag)
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
