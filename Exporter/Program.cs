using System.Diagnostics;
using System.Text.Json;

var options = CliOptions.Parse(args);
var totalStopwatch = Stopwatch.StartNew();

var sourceRoot = Path.GetFullPath(options.SourceRoot);
var outputRoot = Path.GetFullPath(options.OutputDirectory);
var assetsRoot = Path.GetFullPath(options.AssetsDirectory);

Console.WriteLine("Starting exporter...");
Console.WriteLine($"Source: {sourceRoot}");
Console.WriteLine($"Output: {outputRoot}");
Console.WriteLine($"Assets: {assetsRoot}");

if (!Directory.Exists(sourceRoot))
{
  Console.Error.WriteLine($"Source root does not exist: {sourceRoot}");
  return 2;
}

Console.WriteLine("[1/5] Preparing output directory...");
Directory.CreateDirectory(outputRoot);
Directory.CreateDirectory(Path.Combine(outputRoot, "data"));

Console.WriteLine("[2/5] Parsing source data...");
var items = ExporterUtils.RunWithProgress(
  "Parsing items",
  () => ItemParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var recipes = ExporterUtils.RunWithProgress(
  "Parsing recipes",
  () => RecipeParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var blocks = ExporterUtils.RunWithProgress(
  "Parsing blocks",
  () => BlockParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var creatures = ExporterUtils.RunWithProgress(
  "Parsing creatures",
  () => CreatureParser.Parse(sourceRoot),
  result => $"{result.Count} records"
);
var loots = ExporterUtils.RunWithProgress(
  "Parsing loot tables",
  () => LootParser.Parse(sourceRoot),
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
var gameVersion = ExporterUtils.RunWithProgress("Parsing game version", () => GameVersionParser.Parse(sourceRoot));

Console.WriteLine("[3/5] Exporting textures...");
var uiTextureAtlas = new Dictionary<string, (int X, int Y, int Width, int Height)>(StringComparer.Ordinal)
{
  ["btn"] = (0, 0, 16, 16),
  ["btn_hover"] = (16, 0, 16, 16),
  ["btn_pressed"] = (32, 0, 16, 16),
  ["btn_purple"] = (80, 0, 16, 16),
  ["btn_pink"] = (96, 0, 16, 16),
  ["btn_dark"] = (112, 0, 16, 16),
  ["btn_teal"] = (128, 0, 16, 16),
  ["btn_orange"] = (144, 0, 16, 16),
  ["btn_red"] = (256, 0, 16, 16),
  ["btn_active"] = (416, 0, 16, 16),
  ["btn_positive"] = (496, 0, 16, 16),
  ["btn_negative"] = (480, 0, 16, 16),
  ["hover"] = (384, 16, 16, 16),
  ["panel"] = (400, 0, 16, 16),
  ["card"] = (512, 0, 16, 16),
  ["card_hover"] = (528, 0, 16, 16),
  ["card_pressed"] = (544, 0, 16, 16),
  ["card_negative"] = (576, 0, 16, 16),
  ["ingredients"] = (48, 48, 16, 16),
  ["ribbon"] = (80, 48, 16, 16),
  ["dialog"] = (0, 96, 16, 16),
  ["dialog_purple"] = (16, 96, 16, 16),
  ["dialog_pink"] = (32, 96, 16, 16),
  ["dialog_yellow"] = (48, 96, 16, 16),
  ["dialog_teal"] = (64, 96, 16, 16),
  ["dialog_blue"] = (80, 96, 16, 16),
  ["dialog_green"] = (112, 96, 16, 16),
  ["dialog_red"] = (144, 96, 16, 16),
  ["input"] = (336, 16, 16, 16),
  ["input_hover"] = (352, 16, 16, 16),
  ["input_active"] = (368, 16, 16, 16),
  ["slot_hover"] = (223, 47, 18, 18),
  ["slot_favourite"] = (255, 95, 18, 18),
  ["slot_empty"] = (191, 47, 18, 18),
  ["slot_helmet"] = (0, 256, 16, 16),
  ["slot_chestplate"] = (16, 256, 16, 16),
  ["slot_greaves"] = (32, 256, 16, 16),
  ["slot_trinket"] = (48, 256, 16, 16),
  ["slot_ammo"] = (64, 256, 16, 16),
  ["slot_trash"] = (80, 256, 16, 16),
  ["slot_sell"] = (96, 256, 16, 16),
  ["slot_currency"] = (96, 256, 16, 16),
  ["effect_buff"] = (384, 0, 18, 18),
  ["effect_neutral"] = (384, 32, 18, 18),
  ["effect_debuff"] = (384, 64, 18, 18),
  ["small_heart"] = (144, 24, 7, 7),
  ["small_energy"] = (144, 32, 7, 7),
  ["small_defence"] = (144, 40, 7, 7),
  ["heart_quarter"] = (2, 66, 11, 10),
  ["heart_half"] = (18, 66, 11, 10),
  ["heart_three_quarters"] = (34, 66, 11, 10),
  ["heart_full"] = (50, 66, 11, 10),
  ["heart_empty"] = (66, 66, 11, 10),
  ["tooltip_ranged"] = (0, 320, 8, 8),
  ["tooltip_melee"] = (8, 320, 8, 8),
  ["tooltip_defence"] = (16, 320, 8, 8),
  ["tooltip_cooldown"] = (24, 320, 8, 8),
  ["tooltip_trinket"] = (32, 320, 8, 8),
  ["tooltip_heal"] = (40, 320, 8, 8),
  ["tooltip_placeable"] = (48, 320, 8, 8),
  ["tooltip_consumable"] = (56, 320, 8, 8),
  ["tooltip_pickaxe"] = (64, 320, 8, 8),
  ["tooltip_axe"] = (72, 320, 8, 8),
  ["tooltip_arrow"] = (80, 320, 8, 8),
  ["tooltip_smile"] = (88, 320, 8, 8),
  ["tooltip_block"] = (96, 320, 8, 8),
  ["tooltip_knockback"] = (104, 320, 8, 8),
  ["tooltip_locked"] = (112, 320, 8, 8),
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
  uiTextureAtlas[effectTextureName] = (textureX, textureY, 16, 16);
}

var copiedItemTextures = ExporterUtils.RunWithProgress(
  "Converting item textures to WEBP",
  () =>
    TextureExportUtils.CopyTextures(
      entries: items,
      sourceDirectory: Path.Combine(assetsRoot, "textures", "atlas", "items"),
      destinationDirectory: Path.Combine(outputRoot, "assets", "items")
    ),
  result => $"{result} copied"
);
var copiedBlockTextures = ExporterUtils.RunWithProgress(
  "Converting block textures to WEBP",
  () =>
    TextureExportUtils.CopyTextures(
      entries: blocks,
      sourceDirectory: Path.Combine(assetsRoot, "textures", "atlas", "items"),
      destinationDirectory: Path.Combine(outputRoot, "assets", "blocks")
    ),
  result => $"{result} copied"
);
var slicedUiTextures = ExporterUtils.RunWithProgress(
  "Slicing UI atlas textures to WEBP",
  () =>
    TextureExportUtils.SliceTextureAtlasToWebp(
      sourcePath: Path.Combine(assetsRoot, "textures", "ui.png"),
      destinationDirectory: Path.Combine(outputRoot, "assets", "ui"),
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
  blockCount = blocks.Count,
  creatureCount = creatures.Count,
  lootCount = loots.Count,
  spawnCount = spawns.Count,
  effectCount = effects.Count,
};

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

Console.WriteLine("[4/5] Parsing and writing translations...");

var parsedTranslationsCount = ExporterUtils.RunWithProgress(
  "Parsing translations",
  () =>
    ExporterUtils.ParseTranslationsToJson(
      Path.Combine(assetsRoot, "translations", "en-AU", "keys.txt"),
      Path.Combine(outputRoot, "data", "translations.json"),
      jsonOptions
    ),
  result => result.HasValue ? $"{result.Value} records" : "source not found"
);

Console.WriteLine("[5/5] Writing JSON files...");
ExporterUtils.RunActionWithProgress(
  "Writing items.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputRoot, "data", "items.json"), items, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing recipes.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputRoot, "data", "recipes.json"), recipes, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing blocks.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputRoot, "data", "blocks.json"), blocks, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing creatures.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputRoot, "data", "creatures.json"), creatures, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing loot.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputRoot, "data", "loot.json"), loots, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing spawn.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputRoot, "data", "spawn.json"), spawns, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing effects.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputRoot, "data", "effects.json"), effects, jsonOptions)
);
ExporterUtils.RunActionWithProgress(
  "Writing summary.json",
  () => ExporterUtils.WriteJson(Path.Combine(outputRoot, "data", "summary.json"), summary, jsonOptions)
);

totalStopwatch.Stop();

Console.WriteLine($"Export complete. Wrote JSON files to: {outputRoot}");
Console.WriteLine(
  $"Items: {items.Count}, Recipes: {recipes.Count}, Blocks: {blocks.Count}, Creatures: {creatures.Count}, Loot: {loots.Count}, Spawns: {spawns.Count}, Effects: {effects.Count}"
);
Console.WriteLine($"Converted item textures to WEBP: {copiedItemTextures}");
Console.WriteLine($"Converted block textures to WEBP: {copiedBlockTextures}");
Console.WriteLine($"Sliced UI atlas textures to WEBP: {slicedUiTextures} -> ui");
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
