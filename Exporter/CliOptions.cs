internal sealed record CliOptions(string SourceRoot, string OutputDirectory, string AssetsDirectory)
{
  public static CliOptions Parse(string[] args)
  {
    var sourceRoot = Path.Combine(Directory.GetCurrentDirectory(), "Allumeria");
    var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "export");
    var assetsDirectory = Path.Combine("C:\\Program Files (x86)\\Steam\\steamapps\\common\\Allumeria Demo\\res");

    for (var i = 0; i < args.Length; i++)
    {
      var arg = args[i];
      if ((arg == "--source" || arg == "-s") && i + 1 < args.Length)
      {
        sourceRoot = args[++i];
        continue;
      }

      if ((arg == "--output" || arg == "-o") && i + 1 < args.Length)
      {
        outputDirectory = args[++i];
        continue;
      }

      if ((arg == "--assets" || arg == "-a") && i + 1 < args.Length)
      {
        assetsDirectory = args[++i];
        continue;
      }
    }

    return new CliOptions(sourceRoot, outputDirectory, assetsDirectory);
  }
}
