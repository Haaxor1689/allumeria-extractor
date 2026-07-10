using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

internal static class TextureExportUtils
{
  public static int CopyTexturesById(
    IEnumerable<string> textureIds,
    string sourceDirectory,
    string destinationDirectory
  )
  {
    if (!Directory.Exists(sourceDirectory))
      return 0;

    Directory.CreateDirectory(destinationDirectory);

    var copiedCount = 0;
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var textureId in textureIds)
    {
      if (string.IsNullOrWhiteSpace(textureId) || !seen.Add(textureId))
        continue;

      var sourcePath = Path.Combine(sourceDirectory, $"{textureId}.png");
      if (!File.Exists(sourcePath))
        continue;

      var destinationPath = Path.Combine(destinationDirectory, $"{textureId}.webp");
      using var image = Image.Load(sourcePath);
      image.Save(destinationPath, new WebpEncoder());
      copiedCount++;
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
}
