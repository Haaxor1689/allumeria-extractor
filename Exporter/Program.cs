using System.Diagnostics;
using System.Text.Json;

var options = CliOptions.Parse(args);
var totalStopwatch = Stopwatch.StartNew();

var sourceRoot = Path.GetFullPath(options.SourceRoot);
var assetsRoot = Path.GetFullPath(options.AssetsDirectory);
var outputAssetsRoot = Path.GetFullPath(options.OutputAssetsDirectory);
var outputDataRoot = Path.GetFullPath(options.OutputDataDirectory);

Console.WriteLine("Starting exporter...");
Console.WriteLine($"Source: {sourceRoot}");
Console.WriteLine($"Output Assets: {outputAssetsRoot}");
Console.WriteLine($"Output Data: {outputDataRoot}");
Console.WriteLine($"Assets: {assetsRoot}");

if (!Directory.Exists(sourceRoot))
{
  Console.Error.WriteLine($"Source root does not exist: {sourceRoot}");
  return 2;
}

Console.WriteLine("[1/5] Preparing output directory...");
Directory.CreateDirectory(outputAssetsRoot);
Directory.CreateDirectory(outputDataRoot);

Console.WriteLine("[2/5] Parsing source data...");
var blockParseResult = ExporterUtils.RunWithProgress(
  "Parsing blocks",
  () => BlockParser.Parse(sourceRoot),
  result => $"{result.Blocks.Count} blocks, {result.Items.Count} block items"
);
var blocks = blockParseResult.Blocks;
var items = ExporterUtils.RunWithProgress(
  "Parsing items",
  () => ItemParser.Parse(sourceRoot, blockParseResult.Items),
  result => $"{result.Count} records"
);
var recipes = ExporterUtils.RunWithProgress(
  "Parsing recipes",
  () => RecipeParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var recipeAliases = ExporterUtils.RunWithProgress(
  "Parsing recipe aliases",
  () => RecipeAliasParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var creatures = ExporterUtils.RunWithProgress(
  "Parsing creatures",
  () => CreatureParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var loots = ExporterUtils.RunWithProgress(
  "Parsing loot tables",
  () => LootParser.Parse(sourceRoot, blockParseResult.Loots),
  result => $"{result.Count} records"
);
var spawns = ExporterUtils.RunWithProgress(
  "Parsing spawns",
  () => SpawnParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var effects = ExporterUtils.RunWithProgress(
  "Parsing effects",
  () => EffectParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var itemTags = ExporterUtils.RunWithProgress(
  "Parsing item tags",
  () => ItemTagParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var cropTextureVariantCounts = BuildCropTextureVariantCounts(blocks);
var blockModels = ExporterUtils.RunWithProgress(
  "Parsing block models",
  () => BlockModelParser.Parse(sourceRoot, cropTextureVariantCounts),
  result => $"{result.Count} records"
);
var gameVersion = ExporterUtils.RunWithProgress("Parsing game version", () => GameVersionParser.Parse(sourceRoot));

Console.WriteLine("[3/5] Exporting textures...");
var uiTextureAtlas = new Dictionary<string, (int X, int Y, int Width, int Height, string Type)>(StringComparer.Ordinal)
{
  ["btn"] = (0, 0, 16, 16, "ui"),
  ["btn_hover"] = (16, 0, 16, 16, "ui"),
  ["btn_pressed"] = (32, 0, 16, 16, "ui"),
  ["btn_purple"] = (80, 0, 16, 16, "ui"),
  ["btn_pink"] = (96, 0, 16, 16, "ui"),
  ["btn_dark"] = (112, 0, 16, 16, "ui"),
  ["btn_teal"] = (128, 0, 16, 16, "ui"),
  ["btn_orange"] = (144, 0, 16, 16, "ui"),
  ["btn_red"] = (256, 0, 16, 16, "ui"),
  ["btn_active"] = (416, 0, 16, 16, "ui"),
  ["btn_positive"] = (496, 0, 16, 16, "ui"),
  ["btn_negative"] = (480, 0, 16, 16, "ui"),
  ["hover"] = (384, 16, 16, 16, "ui"),
  ["panel"] = (400, 0, 16, 16, "ui"),
  ["card"] = (512, 0, 16, 16, "ui"),
  ["card_hover"] = (528, 0, 16, 16, "ui"),
  ["card_pressed"] = (544, 0, 16, 16, "ui"),
  ["card_negative"] = (576, 0, 16, 16, "ui"),
  ["ingredients"] = (48, 48, 16, 16, "ui"),
  ["ribbon"] = (80, 48, 16, 16, "ui"),
  ["dialog_rarity_0"] = (0, 96, 16, 16, "ui"),
  ["dialog_rarity_1"] = (16, 96, 16, 16, "ui"),
  ["dialog_rarity_2"] = (32, 96, 16, 16, "ui"),
  ["dialog_rarity_3"] = (48, 96, 16, 16, "ui"),
  ["dialog_rarity_4"] = (64, 96, 16, 16, "ui"),
  ["dialog_rarity_5"] = (80, 96, 16, 16, "ui"),
  ["dialog_positive"] = (112, 96, 16, 16, "ui"),
  ["dialog"] = (128, 96, 16, 16, "ui"),
  ["dialog_negative"] = (144, 96, 16, 16, "ui"),
  ["input"] = (336, 16, 16, 16, "ui"),
  ["input_hover"] = (352, 16, 16, 16, "ui"),
  ["input_active"] = (368, 16, 16, 16, "ui"),
  ["scroll_thumb"] = (96, 16, 16, 16, "ui"),
  ["scroll_track"] = (112, 16, 16, 16, "ui"),
  ["slot"] = (191, 47, 18, 18, "ui"),
  ["slot_hover"] = (223, 47, 18, 18, "ui"),
  ["slot_favourite"] = (255, 95, 18, 18, "icons"),
  ["slot_helmet"] = (0, 256, 16, 16, "icons"),
  ["slot_chestplate"] = (16, 256, 16, 16, "icons"),
  ["slot_greaves"] = (32, 256, 16, 16, "icons"),
  ["slot_trinket"] = (48, 256, 16, 16, "icons"),
  ["slot_ammo"] = (64, 256, 16, 16, "icons"),
  ["slot_trash"] = (80, 256, 16, 16, "icons"),
  ["slot_sell"] = (96, 256, 16, 16, "icons"),
  ["slot_currency"] = (96, 256, 16, 16, "icons"),
  ["effect_buff"] = (0, 384, 18, 18, "icons"),
  ["effect_neutral"] = (32, 384, 18, 18, "icons"),
  ["effect_debuff"] = (64, 384, 18, 18, "icons"),
  ["small_heart"] = (144, 24, 7, 7, "icons"),
  ["small_energy"] = (144, 32, 7, 7, "icons"),
  ["small_defence"] = (144, 40, 7, 7, "icons"),
  ["small_attack"] = (144, 48, 7, 7, "icons"),
  ["small_speed"] = (144, 56, 7, 7, "icons"),
  ["small_crit"] = (144, 64, 7, 7, "icons"),
  ["small_heal"] = (144, 72, 7, 7, "icons"),
  ["small_knockback"] = (144, 80, 7, 7, "icons"),
  ["small_ranged"] = (144, 88, 7, 7, "icons"),
  ["heart_quarter"] = (2, 66, 11, 10, "icons"),
  ["heart_half"] = (18, 66, 11, 10, "icons"),
  ["heart_three_quarters"] = (34, 66, 11, 10, "icons"),
  ["heart_full"] = (50, 66, 11, 10, "icons"),
  ["heart_empty"] = (66, 66, 11, 10, "icons"),
  ["category_blocks"] = (21, 352, 6, 6, "icons"),
  ["category_tools"] = (37, 352, 6, 6, "icons"),
  ["category_technical"] = (53, 352, 6, 6, "icons"),
  ["category_weapons"] = (69, 352, 6, 6, "icons"),
  ["category_natural"] = (85, 352, 6, 6, "icons"),
  ["category_items"] = (101, 352, 6, 6, "icons"),
  ["category_decoration"] = (165, 352, 6, 6, "icons"),
};

foreach (var effect in effects)
{
  if (
    effect is not IDictionary<string, object?> effectData
    || !effectData.TryGetValue("id", out var idValue)
    || !effectData.TryGetValue("textureX", out var textureXValue)
    || !effectData.TryGetValue("textureY", out var textureYValue)
    || textureXValue is null
    || textureYValue is null
  )
  {
    continue;
  }

  if (!TryReadInt32(textureXValue, out var textureX) || !TryReadInt32(textureYValue, out var textureY))
    continue;

  if (idValue is null)
    continue;

  var effectTextureName = $"{textureX}x{textureY}";
  uiTextureAtlas[effectTextureName] = (textureX, textureY, 16, 16, "effects");
}

foreach (var itemTag in itemTags)
{
  if (
    itemTag is not IDictionary<string, object?> itemTagData
    || !itemTagData.TryGetValue("iconX", out var iconXValue)
    || !itemTagData.TryGetValue("iconY", out var iconYValue)
    || iconXValue is null
    || iconYValue is null
  )
  {
    continue;
  }

  if (!TryReadInt32(iconXValue, out var iconX) || !TryReadInt32(iconYValue, out var iconY))
    continue;

  var tagTextureName = $"{iconX}x{iconY}";
  uiTextureAtlas[tagTextureName] = (iconX, iconY, 8, 8, "item_tags");
}

var copiedItemTextures = ExporterUtils.RunWithProgress(
  "Converting item textures to WEBP",
  () =>
    TextureExportUtils.CopyTexturesById(
      textureIds: ExtractItemTextureIds(items, ["missing"]),
      sourceDirectory: Path.Combine(assetsRoot, "textures", "atlas", "items"),
      destinationDirectory: Path.Combine(outputAssetsRoot, "items")
    ),
  result => $"{result} copied"
);
var copiedBlockTextures = ExporterUtils.RunWithProgress(
  "Converting block textures to WEBP",
  () =>
    TextureExportUtils.CopyTexturesById(
      textureIds: ExtractBlockTextureIds(blocks),
      sourceDirectory: Path.Combine(assetsRoot, "textures", "atlas", "blocks"),
      destinationDirectory: Path.Combine(outputAssetsRoot, "blocks")
    ),
  result => $"{result} copied"
);
var slicedUiTextures = ExporterUtils.RunWithProgress(
  "Slicing UI atlas textures to WEBP",
  () =>
    TextureExportUtils.SliceTextureAtlasToWebp(
      sourcePath: Path.Combine(assetsRoot, "textures", "ui.png"),
      destinationDirectory: Path.Combine(outputAssetsRoot),
      regions: uiTextureAtlas
    ),
  result => $"{result} sliced"
);

var summary = new
{
  generatedAtUtc = DateTimeOffset.UtcNow,
  gameVersion,
  itemCount = items.Count,
  recipeCount = recipes.Count,
  recipeAliasCount = recipeAliases.Count,
  blockCount = blocks.Count,
  creatureCount = creatures.Count,
  lootCount = loots.Count,
  spawnCount = spawns.Count,
  effectCount = effects.Count,
  itemTagCount = itemTags.Count,
  blockModelCount = blockModels.Count,
};

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

Console.WriteLine("[4/5] Parsing and writing translations...");

var parsedTranslationsCount = ExporterUtils.RunWithProgress(
  "Parsing translations",
  () =>
    ExporterUtils.ParseTranslationsToJson(
      Path.Combine(assetsRoot, "translations", "en-AU", "keys.txt"),
      Path.Combine(outputDataRoot, "translations.json"),
      jsonOptions
    ),
  result => result.HasValue ? $"{result.Value} records" : "source not found"
);

Console.WriteLine("[5/5] Writing JSON files...");
ExporterUtils.NormalizeMissingSprites(items, Path.Combine(assetsRoot, "textures", "atlas", "items"), "missing");
ExporterUtils.RunActionWithProgress(
  "Writing items.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "items.json"), items, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing recipes.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "recipes.json"), recipes, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing recipe_aliases.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "recipe_aliases.json"), recipeAliases, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing blocks.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "blocks.json"), blocks, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing creatures.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "creatures.json"), creatures, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing loot.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "loot.json"), loots, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing spawn.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "spawn.json"), spawns, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing effects.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "effects.json"), effects, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing item_tags.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "item_tags.json"), itemTags, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing block_models.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "block_models.json"), blockModels, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing summary.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputDataRoot, "summary.json"), summary, jsonOptions)
);

totalStopwatch.Stop();

Console.WriteLine($"Export complete. Wrote JSON files to: {outputDataRoot}");
Console.WriteLine(
  $"Items: {items.Count}, Recipes: {recipes.Count}, RecipeAliases: {recipeAliases.Count}, Blocks: {blocks.Count}, Creatures: {creatures.Count}, Loot: {loots.Count}, Spawns: {spawns.Count}, Effects: {effects.Count}, ItemTags: {itemTags.Count}, BlockModels: {blockModels.Count}"
);
Console.WriteLine($"Converted item textures to WEBP: {copiedItemTextures}");
Console.WriteLine($"Converted block textures to WEBP: {copiedBlockTextures}");
Console.WriteLine(
  $"Sliced UI atlas textures to WEBP: {string.Join(", ", slicedUiTextures.Select(kv => $"{kv.Key}: {kv.Value}"))}"
);
if (parsedTranslationsCount.HasValue)
  Console.WriteLine($"Parsed translations: {parsedTranslationsCount.Value}");
else
  Console.WriteLine($"Skipped translations parse (not found)");
Console.WriteLine($"Total time: {totalStopwatch.Elapsed.TotalSeconds:F2}s");

return 0;

static bool TryReadInt32(object value, out int result)
{
  switch (value)
  {
    case int intValue:
      result = intValue;
      return true;
    case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
      result = (int)longValue;
      return true;
    case string stringValue when int.TryParse(stringValue, out var parsed):
      result = parsed;
      return true;
    default:
      result = default;
      return false;
  }
}

static IEnumerable<string> ExtractBlockTextureIds(IEnumerable<object> blocks)
{
  foreach (var block in blocks)
  {
    if (block is not IDictionary<string, object?> blockData)
      continue;

    if (blockData.TryGetValue("texture", out var textureValue))
    {
      var texture = textureValue?.ToString();
      if (!string.IsNullOrWhiteSpace(texture))
        yield return texture;
    }

    if (!blockData.TryGetValue("textures", out var texturesValue) || texturesValue is null)
      continue;

    if (texturesValue is IEnumerable<string> stringTextures)
    {
      foreach (var texture in stringTextures)
      {
        if (!string.IsNullOrWhiteSpace(texture))
          yield return texture;
      }
      continue;
    }

    if (texturesValue is IEnumerable<object?> objectTextures)
    {
      foreach (var texture in objectTextures)
      {
        var textureId = texture?.ToString();
        if (!string.IsNullOrWhiteSpace(textureId))
          yield return textureId;
      }
    }
  }
}

static IEnumerable<string> ExtractItemTextureIds(IEnumerable<object> items, IEnumerable<string>? extraTextureIds = null)
{
  var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

  if (extraTextureIds is not null)
  {
    foreach (var extraId in extraTextureIds)
    {
      if (!string.IsNullOrWhiteSpace(extraId) && seen.Add(extraId))
        yield return extraId;
    }
  }

  foreach (var item in items)
  {
    if (item is not IDictionary<string, object?> itemData)
      continue;

    if (itemData.TryGetValue("id", out var idValue))
    {
      var id = idValue?.ToString();
      if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
        yield return id;
    }

    if (itemData.TryGetValue("sprite", out var spriteValue))
    {
      var sprite = spriteValue?.ToString();
      if (!string.IsNullOrWhiteSpace(sprite) && seen.Add(sprite))
        yield return sprite;
    }
  }
}

static IReadOnlyDictionary<string, int> BuildCropTextureVariantCounts(IEnumerable<object> blocks)
{
  var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

  foreach (var block in blocks)
  {
    if (block is not IDictionary<string, object?> blockData)
      continue;

    var blockClass = ReadBlockClass(blockData);
    if (!string.Equals(blockClass, "Crop", StringComparison.Ordinal))
      continue;

    if (
      blockData.TryGetValue("specialModel", out var specialModelValue)
      && specialModelValue is bool specialModel
      && specialModel
    )
      continue;

    if (!blockData.TryGetValue("blockModel", out var blockModelValue) || blockModelValue is not string blockModel)
      continue;

    blockData.TryGetValue("textures", out var texturesValue);
    var textureCount = CountTextures(texturesValue);
    if (textureCount <= 1)
      continue;

    if (!result.TryGetValue(blockModel, out var currentCount) || textureCount > currentCount)
      result[blockModel] = textureCount;
  }

  return result;
}

static string? ReadBlockClass(IDictionary<string, object?> blockData)
{
  if (blockData.TryGetValue("class", out var classValue) && classValue is string className)
    return className;

  if (blockData.TryGetValue("type", out var legacyTypeValue) && legacyTypeValue is string legacyTypeName)
    return legacyTypeName;

  return null;
}

static int CountTextures(object? texturesValue)
{
  var count = 0;

  if (texturesValue is IEnumerable<string> stringTextures)
  {
    foreach (var texture in stringTextures)
    {
      if (!string.IsNullOrWhiteSpace(texture))
        count++;
    }

    return count;
  }

  if (texturesValue is IEnumerable<object?> objectTextures)
  {
    foreach (var texture in objectTextures)
    {
      if (!string.IsNullOrWhiteSpace(texture?.ToString()))
        count++;
    }
  }

  return count;
}
