using Allumeria.Comfort;

internal class NPCDataEntry : Dictionary<string, object?>
{
  public NPCDataEntry(NPCData entry)
  {
    this["id"] = entry.stringID;

    this["entity"] = entry.entityType.Name;

    CatalogueParser.entries.TryGetValue(entry.shopCatalogue, out var catalogueEntry);
    if (catalogueEntry != null)
      this["catalogue"] = catalogueEntry.Id;

    ComfortRequirementParser.entries.TryGetValue(entry.comfortRequirements, out var comfortEntry);
    if (comfortEntry != null)
      this["comfortRequirements"] = comfortEntry.Id;
  }
}

internal static class NPCDataParser
{
  public static Dictionary<NPCData, NPCDataEntry> entries = [];

  public static Dictionary<NPCData, NPCDataEntry> Parse()
  {
    foreach (var npcData in NPCData.allNPCS)
      entries[npcData] = new NPCDataEntry(npcData);

    return entries;
  }
}
