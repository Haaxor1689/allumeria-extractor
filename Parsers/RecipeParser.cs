using System.Reflection;
using Allumeria.Items;
using Allumeria.Items.Crafting;

internal class CraftingRecipeEntry : Dictionary<string, object?>
{
  public CraftingRecipeEntry(CraftingRecipe recipe)
  {
    if (Reflection.GetPrivate<Item>(recipe.result, "item", out var item))
      this["result"] = item.strID;

    this["amount"] = recipe.result.amount;

    this["station"] = recipe.requiredStation.strID;

    this["requirements"] = BuildReqDictionary(recipe);
  }

  private static Dictionary<string, object?> BuildReqDictionary(CraftingRecipe recipe)
  {
    return recipe
      .requiredItems.GroupBy(e => e.alias?.strID ?? e.item.strID)
      .Select(group =>
      {
        var last = group.Last();
        var key = last.alias?.strID ?? last.item.strID;
        return new KeyValuePair<string, object?>(key, last.amount);
      })
      .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
      .ToDictionary(pair => pair.Key, pair => pair.Value);
  }
}

internal static class RecipeParser
{
  public static Dictionary<CraftingRecipe, CraftingRecipeEntry> entries = [];

  public static Dictionary<CraftingRecipe, CraftingRecipeEntry> Parse()
  {
    CraftingRecipe.InitCraftingRecipes();

    var recipes = CraftingRecipe.recipes.Where(r => r != null);

    foreach (var recipe in recipes)
      entries[recipe] = new CraftingRecipeEntry(recipe);

    return entries;
  }
}
