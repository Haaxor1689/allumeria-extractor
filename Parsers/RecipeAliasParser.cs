using Allumeria.Items.Crafting;

internal class RecipeAliasEntry : Dictionary<string, object?>
{
  public RecipeAliasEntry(RecipeAlias alias, string id)
  {
    this["id"] = id;

    this["items"] = alias.items.Select(item => item.strID).ToList();
  }
}

internal static class RecipeAliasParser
{
  public static Dictionary<RecipeAlias, RecipeAliasEntry> entries = [];

  public static Dictionary<RecipeAlias, RecipeAliasEntry> Parse()
  {
    var aliasMap = Reflection.BuildStaticInstanceNameMap<RecipeAlias>();

    foreach (var (alias, id) in aliasMap)
      entries[alias] = new RecipeAliasEntry(alias, id);

    return entries;
  }
}
