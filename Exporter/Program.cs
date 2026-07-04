using System.Text;
using System.Text.Json;

var options = CliOptions.Parse(args);

var sourceRoot = Path.GetFullPath(options.SourceRoot);
var outputRoot = Path.GetFullPath(options.OutputDirectory);
var assetsRoot = Path.GetFullPath(options.AssetsDirectory);

if (!Directory.Exists(sourceRoot))
{
  Console.Error.WriteLine($"Source root does not exist: {sourceRoot}");
  return 2;
}

Directory.CreateDirectory(outputRoot);

var legacyEntitiesPath = Path.Combine(outputRoot, "entities.json");
if (File.Exists(legacyEntitiesPath))
  File.Delete(legacyEntitiesPath);

var translationsSourcePath = Path.Combine(assetsRoot, "translations", "en-AU", "keys.txt");
var translationsDestinationPath = Path.Combine(outputRoot, "translations.json");

var items = ItemParser.Parse(sourceRoot);
var recipes = RecipeParser.Parse(sourceRoot);
var blocks = BlockParser.Parse(sourceRoot);
var creatures = CreatureParser.Parse(sourceRoot);
var loots = LootParser.Parse(sourceRoot);
var spawns = SpawnParser.Parse(sourceRoot);
var effects = EffectParser.Parse(sourceRoot);
var gameVersion = GameVersionParser.Parse(sourceRoot);

var copiedItemTextures = CopyTextures(
  entries: items,
  sourceDirectory: Path.Combine(assetsRoot, "textures", "atlas", "items"),
  destinationDirectory: Path.Combine(outputRoot, "assets", "items")
);
var copiedBlockTextures = CopyTextures(
  entries: blocks,
  sourceDirectory: Path.Combine(assetsRoot, "textures", "atlas", "items"),
  destinationDirectory: Path.Combine(outputRoot, "assets", "blocks")
);
var copiedUiTexture = CopyFileIfExists(
  sourcePath: Path.Combine(assetsRoot, "textures", "ui.png"),
  destinationPath: Path.Combine(outputRoot, "assets", "ui.png")
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

var parsedTranslationsCount = ParseTranslationsToJson(translationsSourcePath, translationsDestinationPath, jsonOptions);

WriteJson(Path.Combine(outputRoot, "items.json"), items, jsonOptions);
WriteJson(Path.Combine(outputRoot, "recipes.json"), recipes, jsonOptions);
WriteJson(Path.Combine(outputRoot, "blocks.json"), blocks, jsonOptions);
WriteJson(Path.Combine(outputRoot, "creatures.json"), creatures, jsonOptions);
WriteJson(Path.Combine(outputRoot, "loot.json"), loots, jsonOptions);
WriteJson(Path.Combine(outputRoot, "spawn.json"), spawns, jsonOptions);
WriteJson(Path.Combine(outputRoot, "effects.json"), effects, jsonOptions);
WriteJson(Path.Combine(outputRoot, "summary.json"), summary, jsonOptions);

Console.WriteLine($"Export complete. Wrote JSON files to: {outputRoot}");
Console.WriteLine(
  $"Items: {items.Count}, Recipes: {recipes.Count}, Blocks: {blocks.Count}, Creatures: {creatures.Count}, Loot: {loots.Count}, Spawns: {spawns.Count}, Effects: {effects.Count}"
);
Console.WriteLine($"Copied item textures: {copiedItemTextures}");
Console.WriteLine($"Copied block textures: {copiedBlockTextures}");
Console.WriteLine(
  copiedUiTexture ? "Copied UI texture: assets/ui.png" : "Skipped UI texture copy (not found): textures/ui.png"
);
if (parsedTranslationsCount.HasValue)
  Console.WriteLine($"Parsed translations: {parsedTranslationsCount.Value} -> {translationsDestinationPath}");
else
  Console.WriteLine($"Skipped translations parse (not found): {translationsSourcePath}");

return 0;

static void WriteJson<T>(string path, T payload, JsonSerializerOptions jsonOptions)
{
  var json = JsonSerializer.Serialize(payload, jsonOptions);
  File.WriteAllText(path, json, Encoding.UTF8);
}

static int CopyTextures(IEnumerable<object> entries, string sourceDirectory, string destinationDirectory)
{
  if (!Directory.Exists(sourceDirectory))
    return 0;

  Directory.CreateDirectory(destinationDirectory);

  var copiedCount = 0;

  foreach (var id in ExtractIds(entries))
  {
    string? sourcePath = null;

    var candidate = Path.Combine(sourceDirectory, $"{id}.png");
    if (File.Exists(candidate))
      sourcePath = candidate;

    if (sourcePath is null)
      continue;

    var destinationPath = Path.Combine(destinationDirectory, $"{id}.png");
    File.Copy(sourcePath, destinationPath, overwrite: true);
    copiedCount++;
  }

  return copiedCount;
}

static bool CopyFileIfExists(string sourcePath, string destinationPath)
{
  if (!File.Exists(sourcePath))
    return false;

  var destinationDirectory = Path.GetDirectoryName(destinationPath);
  if (!string.IsNullOrWhiteSpace(destinationDirectory))
    Directory.CreateDirectory(destinationDirectory);

  File.Copy(sourcePath, destinationPath, overwrite: true);
  return true;
}

static int? ParseTranslationsToJson(string sourcePath, string destinationPath, JsonSerializerOptions jsonOptions)
{
  if (!File.Exists(sourcePath))
    return null;

  var translations = new Dictionary<string, string>(StringComparer.Ordinal);

  foreach (var line in File.ReadLines(sourcePath))
  {
    if (!TryParseTranslationLine(line, out var key, out var value))
      continue;

    translations[key] = value;
  }

  WriteJson(destinationPath, translations, jsonOptions);
  return translations.Count;
}

static bool TryParseTranslationLine(string line, out string key, out string value)
{
  key = string.Empty;
  value = string.Empty;

  var trimmed = line.Trim();
  if (string.IsNullOrWhiteSpace(trimmed))
    return false;

  var splitIndex = trimmed.IndexOfAny([' ', '\t']);
  if (splitIndex <= 0 || splitIndex + 1 >= trimmed.Length)
    return false;

  key = trimmed[..splitIndex].Trim();
  value = trimmed[(splitIndex + 1)..].TrimStart();
  return !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value);
}

static IEnumerable<string> ExtractIds(IEnumerable<object> entries)
{
  var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

  foreach (var entry in entries)
  {
    if (entry is not IDictionary<string, object?> dictionary)
      continue;

    if (!dictionary.TryGetValue("id", out var idValue))
      continue;

    var id = idValue?.ToString();
    if (string.IsNullOrWhiteSpace(id))
      continue;

    if (seen.Add(id))
      yield return id;
  }
}
