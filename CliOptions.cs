internal sealed record CliOptions(string AssetsDirectory, string OutputAssetsDirectory, string OutputDataDirectory)
{
  private const string AllumeriaInstallDirEnvVar = "ALLUMERIA_INSTALL_DIR";

  public static CliOptions Parse(string[] args)
  {
    var allumeriaInstallDir = Environment.GetEnvironmentVariable(AllumeriaInstallDirEnvVar);
    if (string.IsNullOrEmpty(allumeriaInstallDir))
      allumeriaInstallDir = Path.GetFullPath("C:\\Program Files (x86)\\Steam\\steamapps\\common\\Allumeria Demo");

    var outputAssetsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "export", "assets");
    var outputDataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "export", "data");
    var assetsDirectory = Path.Combine(allumeriaInstallDir, "res");

    for (var i = 0; i < args.Length; i++)
    {
      var arg = args[i];

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

    return new CliOptions(assetsDirectory, outputAssetsDirectory, outputDataDirectory);
  }
}
