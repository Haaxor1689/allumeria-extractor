using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

internal static class TextureExportUtils
{
  public static int CopyTextures(
    IEnumerable<object> entries,
    string sourceDirectory,
    string destinationDirectory,
    IEnumerable<string>? extraTextureIds = null
  )
  {
    if (!Directory.Exists(sourceDirectory))
      return 0;

    Directory.CreateDirectory(destinationDirectory);

    var copiedCount = 0;
    var missingIds = new List<string>();

    foreach (var (id, optional) in ExtractIds(entries, extraTextureIds))
    {
      string? sourcePath = null;

      var candidate = Path.Combine(sourceDirectory, $"{id}.png");
      if (File.Exists(candidate))
        sourcePath = candidate;

      if (sourcePath is null)
      {
        if (!optional)
          missingIds.Add(id);
        continue;
      }

      var destinationPath = Path.Combine(destinationDirectory, $"{id}.webp");
      using var image = Image.Load(sourcePath);
      image.Save(destinationPath, new WebpEncoder());
      copiedCount++;
    }

    if (missingIds.Count > 0)
    {
      Console.WriteLine($"  Missing textures ({missingIds.Count}):");
      foreach (var id in missingIds)
        Console.WriteLine($"    - {id}");
    }

    return copiedCount;
  }

  public static Dictionary<string, int> SliceTextureAtlasToWebp(
    string sourcePath,
    string destinationDirectory,
    IReadOnlyDictionary<string, (int X, int Y, int Width, int Height, string Type)> regions
  )
  {
    if (!File.Exists(sourcePath) || regions.Count == 0)
      return new Dictionary<string, int>();

    Directory.CreateDirectory(destinationDirectory);

    using var atlas = Image.Load<Rgba32>(sourcePath);
    var atlasPixels = new Rgba32[atlas.Width * atlas.Height];
    atlas.CopyPixelDataTo(atlasPixels);
    var slicedCount = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var (name, region) in regions)
    {
      if (
        region.X < 0
        || region.Y < 0
        || region.Width <= 0
        || region.Height <= 0
        || region.X + region.Width > atlas.Width
        || region.Y + region.Height > atlas.Height
      )
      {
        continue;
      }

      if (IsRegionFullyTransparent(atlasPixels, atlas.Width, region.X, region.Y, region.Width, region.Height))
        continue;

      using var slice = atlas.Clone(ctx => ctx.Crop(new Rectangle(region.X, region.Y, region.Width, region.Height)));
      var destinationPath = Path.Combine(destinationDirectory, region.Type, $"{name}.webp");
      Directory.CreateDirectory(Path.Combine(destinationDirectory, region.Type));
      slice.Save(destinationPath, new WebpEncoder());
      if (!slicedCount.ContainsKey(region.Type))
        slicedCount[region.Type] = 0;
      slicedCount[region.Type]++;
    }

    return slicedCount;
  }

  private static bool IsRegionFullyTransparent(
    IReadOnlyList<Rgba32> pixels,
    int imageWidth,
    int x,
    int y,
    int width,
    int height
  )
  {
    for (var py = y; py < y + height; py++)
    {
      for (var px = x; px < x + width; px++)
      {
        var index = py * imageWidth + px;
        if (pixels[index].A > 0)
          return false;
      }
    }

    return true;
  }

  private static IEnumerable<(string Id, bool Optional)> ExtractIds(IEnumerable<object> entries, IEnumerable<string>? extraTextureIds)
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (extraTextureIds is not null)
    {
      foreach (var extraId in extraTextureIds)
      {
        if (string.IsNullOrWhiteSpace(extraId))
          continue;

        if (seen.Add(extraId))
          yield return (extraId, false);
      }
    }

    foreach (var entry in entries)
    {
      if (entry is not IDictionary<string, object?> dictionary)
        continue;

      var hasSprite = dictionary.TryGetValue("sprite", out var spriteValue)
        && !string.IsNullOrWhiteSpace(spriteValue?.ToString());

      if (dictionary.TryGetValue("id", out var idValue))
      {
        var id = idValue?.ToString();
        if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
          yield return (id, hasSprite);
      }

      if (hasSprite)
      {
        var sprite = spriteValue!.ToString()!;
        if (seen.Add(sprite))
          yield return (sprite, false);
      }
    }
  }
}
