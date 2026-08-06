using Allumeria.Items.Crafting;

internal class ShopEntryData : Dictionary<string, object?>
{
  public ShopEntryData(ShopEntry entry)
  {
    if (entry.item != null)
      this["item"] = entry.item.strID;

    if (entry.amount > 0)
      this["amount"] = entry.amount;

    if (entry.price > 0)
      this["price"] = entry.price;
  }
}

internal class CatalogueEntry : Dictionary<string, object?>
{
  public CatalogueEntry(string id, Catalogue catalogue)
  {
    this["id"] = id;

    var entries = catalogue.entries.Select(e => new ShopEntryData(e)).ToList();
    if (entries.Count > 0)
      this["entries"] = entries;
  }
}

internal static class CatalogueParser
{
  public static Dictionary<Catalogue, CatalogueEntry> entries = [];

  public static Dictionary<Catalogue, CatalogueEntry> Parse()
  {
    var catalogueMap = Reflection.BuildStaticInstanceNameMap<Catalogue>();

    foreach (var (catalogue, id) in catalogueMap)
      entries[catalogue] = new CatalogueEntry(id, catalogue);

    return entries;
  }
}
