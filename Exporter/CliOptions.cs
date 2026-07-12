internal sealed record CliOptions(
  string SourceRoot,
  string AssetsDirectory,
  string OutputAssetsDirectory,
  string OutputDataDirectory
)
{
  public static CliOptions Parse(string[] args)
  {
    var sourceRoot = Path.Combine(Directory.GetCurrentDirectory(), "Allumeria");
    var outputAssetsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "export", "assets");
    var outputDataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "export", "data");
    var assetsDirectory = Path.Combine("C:\\Program Files (x86)\\Steam\\steamapps\\common\\Allumeria Demo\\res");

    for (var i = 0; i < args.Length; i++)
    {
      var arg = args[i];
      if ((arg == "--source" || arg == "-s") && i + 1 < args.Length)
      {
        sourceRoot = args[++i];
        continue;
      }

      if ((arg == "--assets" || arg == "-a") && i + 1 < args.Length)
      {
        assetsDirectory = args[++i];
        continue;
      }

      if ((arg == "--out-assets" || arg == "-oa") && i + 1 < args.Length)
      {
        outputAssetsDirectory = args[++i];
        continue;
      }

      if ((arg == "--out-data" || arg == "-od") && i + 1 < args.Length)
      {
        outputDataDirectory = args[++i];
        continue;
      }
    }

    return new CliOptions(sourceRoot, assetsDirectory, outputAssetsDirectory, outputDataDirectory);
  }
}
