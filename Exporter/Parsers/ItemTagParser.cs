using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ItemTagParser
{
  private const string ItemTagTypesRelativePath = "Items\\ItemTagTypes";

  public static List<object> Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "ItemTagTypes", "ItemTag.cs");
    if (!File.Exists(path))
      return [];

    var defaultsByType = BuildDerivedTypeDefaults(sourceRoot);
    defaultsByType["ItemTag"] = new ItemTagTypeDefaults(false, null, null);

    var root = SyntaxParsingUtils.ParseCompilationUnit(path);
    var entries = new List<object>();

    foreach (var field in SyntaxParsingUtils.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var initializer = variable.Initializer?.Value;
        if (initializer is null)
          continue;

        var ctor = SyntaxParsingUtils.TryGetRootObjectCreation(initializer);
        if (ctor is null)
          continue;

        var constructorType = NormalizeTypeName(ctor.Type.ToString());
        if (!constructorType.StartsWith("ItemTag", StringComparison.Ordinal))
          continue;

        var id = variable.Identifier.Text;
        var label = TryReadStringArg(ctor, 0);
        if (string.IsNullOrWhiteSpace(label))
          label = id;

        defaultsByType.TryGetValue(constructorType, out var defaults);
        defaults ??= new ItemTagTypeDefaults(false, null, null);

        var hasIcon = TryReadBoolArg(ctor, 2) ?? defaults.HasIcon;

        var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
          ["id"] = id,
          ["label"] = label,
        };

        if (hasIcon)
        {
          var iconX = TryReadIntArg(ctor, 3) ?? defaults.IconX ?? 0;
          var iconY = TryReadIntArg(ctor, 4) ?? defaults.IconY ?? 0;
          entry["iconX"] = iconX;
          entry["iconY"] = iconY;
        }

        entries.Add(entry);
      }
    }

    return entries;
  }

  private static Dictionary<string, ItemTagTypeDefaults> BuildDerivedTypeDefaults(string sourceRoot)
  {
    var defaults = new Dictionary<string, ItemTagTypeDefaults>(StringComparer.OrdinalIgnoreCase);
    var tagTypesPath = Path.Combine(sourceRoot, ItemTagTypesRelativePath);

    if (!Directory.Exists(tagTypesPath))
      return defaults;

    foreach (var file in Directory.EnumerateFiles(tagTypesPath, "*.cs", SearchOption.TopDirectoryOnly))
    {
      var text = File.ReadAllText(file);
      foreach (Match match in Regex.Matches(text, @"class\s+(?<type>\w+)[^\n\r\{]*:\s*ItemTag\s*\((?<args>[^\)]*)\)"))
      {
        var typeName = match.Groups["type"].Value;
        var args = SplitTopLevelArguments(match.Groups["args"].Value);
        if (args.Count < 5)
          continue;

        var hasIcon = TryParseBoolToken(args[2]);
        var iconX = TryParseIntToken(args[3]);
        var iconY = TryParseIntToken(args[4]);

        if (hasIcon is null && iconX is null && iconY is null)
          continue;

        defaults[typeName] = new ItemTagTypeDefaults(hasIcon ?? false, iconX, iconY);
      }
    }

    return defaults;
  }

  private static List<string> SplitTopLevelArguments(string text)
  {
    var args = new List<string>();
    var start = 0;
    var depth = 0;

    for (var index = 0; index < text.Length; index++)
    {
      var ch = text[index];
      switch (ch)
      {
        case '(': depth++; break;
        case ')': depth--; break;
        case ',' when depth == 0:
          args.Add(text[start..index].Trim());
          start = index + 1;
          break;
      }
    }

    if (start < text.Length)
      args.Add(text[start..].Trim());

    return args;
  }

  private static bool? TryReadBoolArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var expression = TryReadCtorArgExpression(creation, argumentIndex);
    if (expression is null)
      return null;

    var reduced = Unwrap(expression);
    return reduced switch
    {
      LiteralExpressionSyntax literal when literal.Token.Value is bool value => value,
      _ => null,
    };
  }

  private static int? TryReadIntArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var expression = TryReadCtorArgExpression(creation, argumentIndex);
    if (expression is null)
      return null;

    return TryConvertToInt(TryParseLiteralLikeValue(expression));
  }

  private static string? TryReadStringArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var expression = TryReadCtorArgExpression(creation, argumentIndex);
    if (expression is null)
      return null;

    return TryParseLiteralLikeValue(expression) as string;
  }

  private static ExpressionSyntax? TryReadCtorArgExpression(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var args = creation.ArgumentList?.Arguments;
    if (args is null)
      return null;

    var namedExpression = argumentIndex switch
    {
      2 => args.Value.FirstOrDefault(arg => arg.NameColon?.Name.Identifier.Text == "hasIcon")?.Expression,
      3 => args.Value.FirstOrDefault(arg => arg.NameColon?.Name.Identifier.Text == "iconX")?.Expression,
      4 => args.Value.FirstOrDefault(arg => arg.NameColon?.Name.Identifier.Text == "iconY")?.Expression,
      _ => null,
    };

    if (namedExpression is not null)
      return namedExpression;

    if (args.Value.Count > argumentIndex)
      return args.Value[argumentIndex].Expression;

    return null;
  }

  private static int? TryConvertToInt(object? value)
  {
    return value switch
    {
      int intValue => intValue,
      long longValue when longValue >= int.MinValue && longValue <= int.MaxValue => (int)longValue,
      float floatValue => (int)floatValue,
      double doubleValue => (int)doubleValue,
      decimal decimalValue => (int)decimalValue,
      _ => null,
    };
  }

  private static object? TryParseLiteralLikeValue(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);

    if (
      reduced is PrefixUnaryExpressionSyntax prefix
      && prefix.OperatorToken.Text == "-"
      && TryParseLiteralLikeValue(prefix.Operand) is { } nestedNegativeValue
    )
    {
      return nestedNegativeValue switch
      {
        int value => -value,
        long value => -value,
        float value => -value,
        double value => -value,
        decimal value => -value,
        _ => null,
      };
    }

    return reduced switch
    {
      LiteralExpressionSyntax literal when literal.Token.Value is not null => literal.Token.Value,
      InvocationExpressionSyntax invocation when SyntaxParsingUtils.GetInvocationName(invocation) == "nameof" =>
        invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression switch
        {
          IdentifierNameSyntax identifier => identifier.Identifier.Text,
          MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
          _ => null,
        },
      _ => null,
    };
  }

  private static bool? TryParseBoolToken(string token)
  {
    var normalized = token.Trim();
    return normalized switch
    {
      "true" => true,
      "false" => false,
      _ => null,
    };
  }

  private static int? TryParseIntToken(string token)
  {
    var cleaned = token;
    var commentIndex = cleaned.IndexOf("/*", StringComparison.Ordinal);
    if (commentIndex >= 0)
      cleaned = cleaned[..commentIndex];

    cleaned = cleaned.Trim();
    return int.TryParse(cleaned, out var value) ? value : null;
  }

  private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
  {
    var cursor = expression;
    while (true)
    {
      switch (cursor)
      {
        case ParenthesizedExpressionSyntax parenthesized:
          cursor = parenthesized.Expression;
          continue;
        case CastExpressionSyntax cast:
          cursor = cast.Expression;
          continue;
        default:
          return cursor;
      }
    }
  }

  private static string NormalizeTypeName(string rawTypeName)
  {
    var typeName = rawTypeName.Split('.').Last().Trim();
    var genericMarker = typeName.IndexOf('<');
    return genericMarker >= 0 ? typeName[..genericMarker] : typeName;
  }

  private sealed record ItemTagTypeDefaults(bool HasIcon, int? IconX, int? IconY);
}