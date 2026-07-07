using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static class ExporterUtils
{
  public static void NormalizeMissingSprites(IEnumerable<object> entries, string sourceDirectory, string fallbackSprite)
  {
    if (!Directory.Exists(sourceDirectory))
      return;

    foreach (var entry in entries)
    {
      if (entry is not IDictionary<string, object?> dictionary)
        continue;

      var hasExplicitSprite =
        dictionary.TryGetValue("sprite", out var spriteValue) && !string.IsNullOrWhiteSpace(spriteValue?.ToString());

      var sprite = hasExplicitSprite ? spriteValue!.ToString() : null;
      if (!hasExplicitSprite)
      {
        // Block items without an explicit sprite implicitly use their item id as the sprite id.
        var hasBlock = dictionary.TryGetValue("block", out var blockValue) && !string.IsNullOrWhiteSpace(blockValue?.ToString());
        var hasItemTexture =
          dictionary.TryGetValue("itemTexture", out var itemTextureValue)
          && !string.IsNullOrWhiteSpace(itemTextureValue?.ToString());

        if (!hasBlock || hasItemTexture)
          continue;

        if (!dictionary.TryGetValue("id", out var idValue))
          continue;

        sprite = idValue?.ToString();
      }

      if (string.IsNullOrWhiteSpace(sprite) || sprite.Equals(fallbackSprite, StringComparison.OrdinalIgnoreCase))
        continue;

      var sourcePath = Path.Combine(sourceDirectory, $"{sprite}.png");
      if (!File.Exists(sourcePath))
        dictionary["sprite"] = fallbackSprite;
    }
  }

  public static T RunWithProgress<T>(string label, Func<T> action, Func<T, string>? resultSummary = null)
  {
    var stopwatch = Stopwatch.StartNew();
    var result = action();
    stopwatch.Stop();

    var summary = resultSummary is null ? string.Empty : $", {resultSummary(result)}";
    Console.WriteLine($"  - {label}: {stopwatch.Elapsed.TotalMilliseconds:F0} ms{summary}");

    return result;
  }

  public static void RunActionWithProgress(string label, Action action)
  {
    var stopwatch = Stopwatch.StartNew();
    action();
    stopwatch.Stop();

    Console.WriteLine($"  - {label}: {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
  }

  public static void WriteJson<T>(string path, T payload, JsonSerializerOptions jsonOptions)
  {
    var json = JsonSerializer.Serialize(payload, jsonOptions);
    File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
  }

  public static int? ParseTranslationsToJson(
    string sourcePath,
    string destinationPath,
    JsonSerializerOptions jsonOptions
  )
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

  private static bool TryParseTranslationLine(string line, out string key, out string value)
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
}
