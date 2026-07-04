using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var options = CliOptions.Parse(args);

var sourceRoot = Path.GetFullPath(options.SourceRoot);
var outputRoot = Path.GetFullPath(options.OutputDirectory);

if (!Directory.Exists(sourceRoot))
{
  Console.Error.WriteLine($"Source root does not exist: {sourceRoot}");
  return 2;
}

Directory.CreateDirectory(outputRoot);

var export = SourceExporter.Export(sourceRoot);

WriteJson(Path.Combine(outputRoot, "items.json"), export.Items, export.JsonOptions);
WriteJson(Path.Combine(outputRoot, "recipes.json"), export.Recipes, export.JsonOptions);
WriteJson(Path.Combine(outputRoot, "blocks.json"), export.Blocks, export.JsonOptions);
WriteJson(Path.Combine(outputRoot, "entities.json"), export.Entities, export.JsonOptions);
WriteJson(Path.Combine(outputRoot, "types.json"), export.Types, export.JsonOptions);
WriteJson(Path.Combine(outputRoot, "summary.json"), export.Summary, export.JsonOptions);

Console.WriteLine($"Export complete. Wrote JSON files to: {outputRoot}");
Console.WriteLine(
  $"Items: {export.Items.Count}, Recipes: {export.Recipes.Count}, Blocks: {export.Blocks.Count}, Entities: {export.Entities.Count}, Types: {export.Types.Count}"
);

return 0;

static void WriteJson<T>(string path, T payload, JsonSerializerOptions jsonOptions)
{
  var json = JsonSerializer.Serialize(payload, jsonOptions);
  File.WriteAllText(path, json, Encoding.UTF8);
}

internal sealed record CliOptions(string SourceRoot, string OutputDirectory)
{
  public static CliOptions Parse(string[] args)
  {
    var sourceRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Allumeria");
    var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "export");

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
    }

    return new CliOptions(sourceRoot, outputDirectory);
  }
}

internal static class SourceExporter
{
  private static readonly Regex ItemDeclarationRegex = new(
    @"public\s+static\s+[^=;]+\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<expr>.*?);",
    RegexOptions.Singleline | RegexOptions.Compiled
  );

  private static readonly Regex BlockDeclarationRegex = new(
    @"public\s+static\s+Block(?:[A-Za-z_]\w*)?\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<expr>.*?);",
    RegexOptions.Singleline | RegexOptions.Compiled
  );

  private static readonly Regex NewTypeRegex = new(@"new\s+(?<type>[A-Za-z_]\w*)\s*\(", RegexOptions.Compiled);
  private static readonly Regex NameofRegex = new(@"nameof\s*\(\s*(?<value>[A-Za-z_]\w*)\s*\)", RegexOptions.Compiled);
  private static readonly Regex StringLiteralRegex = new("\"(?<value>[^\"\\r\\n]+)\"", RegexOptions.Compiled);

  public static ExportPayload Export(string sourceRoot)
  {
    var items = ParseItems(sourceRoot);
    var recipes = ParseRecipes(sourceRoot);
    var blocks = ParseBlocks(sourceRoot);
    var entities = ParseEntities(sourceRoot);
    var types = ParseTypes(sourceRoot);

    var jsonOptions = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = true,
    };

    var summary = new ExportSummary(
      DateTimeOffset.UtcNow,
      sourceRoot,
      items.Count,
      recipes.Count,
      blocks.Count,
      entities.Count,
      types.Count
    );

    return new ExportPayload(items, recipes, blocks, entities, types, summary, jsonOptions);
  }

  private static List<ItemExport> ParseItems(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "Item.cs");
    if (!File.Exists(path))
    {
      return new List<ItemExport>();
    }

    var text = File.ReadAllText(path);
    var list = new List<ItemExport>();

    foreach (Match match in ItemDeclarationRegex.Matches(text))
    {
      var expr = match.Groups["expr"].Value;
      var name = match.Groups["name"].Value;
      var id = TryReadName(expr) ?? name;
      var ctorType = TryReadCtorType(expr);
      var tags = Regex
        .Matches(expr, @"\.AddTag\(\s*ItemTag\.(?<tag>[A-Za-z_]\w*)", RegexOptions.Compiled)
        .Cast<Match>()
        .Select(m => m.Groups["tag"].Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
        .ToArray();

      var stackSize = TryReadInt(expr, @"\.SetStackSize\(\s*(?<value>\d+)\s*\)");
      var sellValue = TryReadInt(expr, @"\.SellValue\(\s*(?<value>\d+)\s*\)");
      var hidden = Regex.IsMatch(expr, @"\.Hide\s*\(", RegexOptions.Compiled);

      list.Add(
        new ItemExport(name, id, ctorType, stackSize, sellValue, hidden, tags, InferLineNumber(text, match.Index))
      );
    }

    return list;
  }

  private static List<BlockExport> ParseBlocks(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Blocks", "Blocks", "Block.cs");
    if (!File.Exists(path))
    {
      return new List<BlockExport>();
    }

    var text = File.ReadAllText(path);
    var list = new List<BlockExport>();

    foreach (Match match in BlockDeclarationRegex.Matches(text))
    {
      var expr = match.Groups["expr"].Value;
      var name = match.Groups["name"].Value;
      var id = TryReadName(expr) ?? name;
      var ctorType = TryReadCtorType(expr);
      var material = TryReadToken(expr, @"\.SetMaterial\(\s*BlockMaterial\.(?<value>[A-Za-z_]\w*)\s*\)");
      var spawn = TryReadToken(expr, @"\.SetSpawnEntry\(\s*SpawnDefinition\.(?<value>[A-Za-z_]\w*)\s*,");
      var hidden = Regex.IsMatch(expr, @"\.Hide\s*\(", RegexOptions.Compiled);

      list.Add(new BlockExport(name, id, ctorType, material, spawn, hidden, InferLineNumber(text, match.Index)));
    }

    return list;
  }

  private static List<RecipeExport> ParseRecipes(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "Crafting", "CraftingRecipe.cs");
    if (!File.Exists(path))
    {
      return new List<RecipeExport>();
    }

    var text = File.ReadAllText(path);
    var statements = SplitStatements(text)
      .Where(statement => statement.Text.Contains("new CraftingRecipe(", StringComparison.Ordinal))
      .ToList();

    var list = new List<RecipeExport>(statements.Count);
    var syntheticId = 0;

    foreach (var statement in statements)
    {
      var recipeVar = TryReadToken(statement.Text, @"public\s+static\s+CraftingRecipe\s+(?<value>[A-Za-z_]\w*)\s*=");
      var station = TryReadToken(statement.Text, @"CraftingStation\.(?<value>[A-Za-z_]\w*)\s*\)");
      var result = TryReadToken(statement.Text, @"new\s+ItemStack\(\s*(?<value>[^,\)]+)");
      var resultAmount = TryReadInt(statement.Text, @"new\s+ItemStack\(\s*[^,\)]+\s*,\s*(?<value>\d+)");
      var requirements = Regex
        .Matches(
          statement.Text,
          @"\.AddReq\(\s*new\s+RecipeEntry\(\s*(?<item>[^,\)]+)\s*,\s*(?<amount>\d+)",
          RegexOptions.Compiled
        )
        .Cast<Match>()
        .Select(match => new RecipeRequirementExport(
          match.Groups["item"].Value.Trim(),
          int.Parse(match.Groups["amount"].Value)
        ))
        .ToList();
      var achievement = TryReadToken(statement.Text, @"\.AddAchievement\(\s*Achievement\.(?<value>[A-Za-z_]\w*)\s*\)");

      var id = recipeVar ?? $"recipe_{++syntheticId}";

      list.Add(
        new RecipeExport(id, recipeVar, result, resultAmount, station, achievement, requirements, statement.Line)
      );
    }

    return list;
  }

  private static List<EntityExport> ParseEntities(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "EntitySystem", "Entity.cs");
    if (!File.Exists(path))
    {
      return new List<EntityExport>();
    }

    var text = File.ReadAllText(path);
    var matchCollection = Regex.Matches(
      text,
      @"RegisterEntityType\(\s*typeof\s*\(\s*(?<type>[A-Za-z_]\w*)\s*\)\s*\)\s*;",
      RegexOptions.Compiled
    );

    var entities = new List<EntityExport>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (Match match in matchCollection)
    {
      var type = match.Groups["type"].Value;
      if (!seen.Add(type))
      {
        continue;
      }

      entities.Add(new EntityExport(type, InferLineNumber(text, match.Index)));
    }

    return entities;
  }

  private static List<TypeExport> ParseTypes(string sourceRoot)
  {
    var roots = new[]
    {
      Path.Combine(sourceRoot, "Items"),
      Path.Combine(sourceRoot, "Blocks"),
      Path.Combine(sourceRoot, "EntitySystem"),
    };

    var types = new List<TypeExport>();

    foreach (var root in roots.Where(Directory.Exists))
    {
      foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
      {
        var text = File.ReadAllText(file);
        var namespaceName = TryReadToken(text, @"namespace\s+(?<value>[A-Za-z_][A-Za-z0-9_\.]*)\s*;");

        foreach (
          Match match in Regex.Matches(
            text,
            @"\b(class|struct|interface|enum)\s+(?<name>[A-Za-z_]\w*)(?:\s*:\s*(?<base>[^\{\r\n]+))?",
            RegexOptions.Compiled
          )
        )
        {
          var kind = match.Groups[1].Value;
          var name = match.Groups["name"].Value;
          var baseTypes = match.Groups["base"].Success
            ? match
              .Groups["base"]
              .Value.Split(',')
              .Select(token => token.Trim())
              .Where(token => token.Length > 0)
              .ToArray()
            : Array.Empty<string>();

          var relativePath = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');

          types.Add(
            new TypeExport(name, kind, namespaceName, relativePath, baseTypes, InferLineNumber(text, match.Index))
          );
        }
      }
    }

    return types
      .OrderBy(t => t.Namespace, StringComparer.OrdinalIgnoreCase)
      .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static string? TryReadCtorType(string expression)
  {
    var match = NewTypeRegex.Match(expression);
    return match.Success ? match.Groups["type"].Value : null;
  }

  private static string? TryReadName(string expression)
  {
    var nameofMatch = NameofRegex.Match(expression);
    if (nameofMatch.Success)
    {
      return nameofMatch.Groups["value"].Value;
    }

    var stringMatch = StringLiteralRegex.Match(expression);
    return stringMatch.Success ? stringMatch.Groups["value"].Value : null;
  }

  private static string? TryReadToken(string text, string pattern)
  {
    var match = Regex.Match(text, pattern, RegexOptions.Compiled | RegexOptions.Singleline);
    return match.Success ? match.Groups["value"].Value.Trim() : null;
  }

  private static int? TryReadInt(string text, string pattern)
  {
    var value = TryReadToken(text, pattern);
    if (value is null)
    {
      return null;
    }

    return int.TryParse(value, out var parsed) ? parsed : null;
  }

  private static int InferLineNumber(string sourceText, int index)
  {
    if (index <= 0)
    {
      return 1;
    }

    var line = 1;
    for (var i = 0; i < index && i < sourceText.Length; i++)
    {
      if (sourceText[i] == '\n')
      {
        line++;
      }
    }

    return line;
  }

  private static List<StatementSpan> SplitStatements(string source)
  {
    var spans = new List<StatementSpan>();
    var builder = new StringBuilder();
    var startLine = 1;
    var currentLine = 1;
    var depthParen = 0;
    var depthBracket = 0;
    var inString = false;
    var inChar = false;
    var escaped = false;

    for (var i = 0; i < source.Length; i++)
    {
      var c = source[i];
      builder.Append(c);

      if (c == '\n')
      {
        currentLine++;
      }

      if (inString)
      {
        if (!escaped && c == '"')
        {
          inString = false;
        }

        escaped = !escaped && c == '\\';
        continue;
      }

      if (inChar)
      {
        if (!escaped && c == '\'')
        {
          inChar = false;
        }

        escaped = !escaped && c == '\\';
        continue;
      }

      escaped = false;

      switch (c)
      {
        case '"':
          inString = true;
          break;
        case '\'':
          inChar = true;
          break;
        case '(':
          depthParen++;
          break;
        case ')':
          depthParen = Math.Max(0, depthParen - 1);
          break;
        case '[':
          depthBracket++;
          break;
        case ']':
          depthBracket = Math.Max(0, depthBracket - 1);
          break;
        case ';':
          if (depthParen == 0 && depthBracket == 0)
          {
            var text = builder.ToString().Trim();
            if (text.Length > 0)
            {
              spans.Add(new StatementSpan(text, startLine));
            }

            builder.Clear();
            startLine = currentLine;
          }
          break;
      }
    }

    var tail = builder.ToString().Trim();
    if (tail.Length > 0)
    {
      spans.Add(new StatementSpan(tail, startLine));
    }

    return spans;
  }
}

internal sealed record StatementSpan(string Text, int Line);

internal sealed record ExportPayload(
  List<ItemExport> Items,
  List<RecipeExport> Recipes,
  List<BlockExport> Blocks,
  List<EntityExport> Entities,
  List<TypeExport> Types,
  ExportSummary Summary,
  JsonSerializerOptions JsonOptions
);

internal sealed record ExportSummary(
  DateTimeOffset GeneratedAtUtc,
  string SourceRoot,
  int ItemCount,
  int RecipeCount,
  int BlockCount,
  int EntityCount,
  int TypeCount
);

internal sealed record ItemExport(
  string Symbol,
  string Id,
  string? ConstructorType,
  int? StackSize,
  int? SellValue,
  bool Hidden,
  IReadOnlyList<string> Tags,
  int SourceLine
);

internal sealed record BlockExport(
  string Symbol,
  string Id,
  string? ConstructorType,
  string? Material,
  string? SpawnDefinition,
  bool Hidden,
  int SourceLine
);

internal sealed record RecipeExport(
  string Id,
  string? Symbol,
  string? ResultExpression,
  int? ResultAmount,
  string? Station,
  string? Achievement,
  IReadOnlyList<RecipeRequirementExport> Requirements,
  int SourceLine
);

internal sealed record RecipeRequirementExport(string ItemExpression, int Amount);

internal sealed record EntityExport(string TypeName, int SourceLine);

internal sealed record TypeExport(
  string Name,
  string Kind,
  string? Namespace,
  string RelativePath,
  IReadOnlyList<string> BaseTypes,
  int SourceLine
);
