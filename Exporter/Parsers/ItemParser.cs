using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ItemParser
{
  private const string ItemTypesRelativePath = "Items\\ItemTypes";

  public static List<object> Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "Item.cs");
    if (!File.Exists(path))
      return [];

    var root = SyntaxParsingHelpers.ParseCompilationUnit(path);
    var list = new List<object>();
    var constructorParamsByType = BuildConstructorParameterMap(sourceRoot);

    foreach (var field in SyntaxParsingHelpers.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var initializer = variable.Initializer?.Value;
        if (initializer is null)
          continue;

        var ctor = SyntaxParsingHelpers.TryGetRootObjectCreation(initializer);
        if (ctor is null)
          continue;

        var invocations = SyntaxParsingHelpers.FindInvocations(initializer).ToArray();
        var symbol = variable.Identifier.Text;
        var id = SyntaxParsingHelpers.TryReadIdFromObjectCreation(ctor) ?? symbol;
        var constructorType = ctor.Type.ToString();
        var typeName = NormalizeTypeName(constructorType);

        var tags = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "AddTag")
          .Select(inv => new
          {
            key = SyntaxParsingHelpers.TryReadQualifiedMemberArg(inv, 0, "ItemTag"),
            value = TryReadExpressionArg(inv, 1) ?? true,
          })
          .Where(entry => !string.IsNullOrWhiteSpace(entry.key))
          .GroupBy(entry => entry.key!, StringComparer.OrdinalIgnoreCase)
          .Select(group => new KeyValuePair<string, object?>(group.Key, group.Last().value))
          .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
          .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var stackSize = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "SetStackSize")
          .Select(inv => SyntaxParsingHelpers.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var sellValue = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "SellValue")
          .Select(inv => SyntaxParsingHelpers.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var hidden = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "Hide");
        var sweeping = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "MakeSweeping");
        var targetLiquid = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "TargetLiquid");

        var swingAnim = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) is "SetSwingAnim" or "SetSwingAnimation")
          .Select(inv => SyntaxParsingHelpers.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var modelInvocation = invocations.FirstOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetModel"
        );
        var itemModel = TryReadExpressionArg(modelInvocation, 0) as string;
        var itemTexture = TryReadExpressionArg(modelInvocation, 1) as string;

        var currencyAmount = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "IsCurrency")
          .Select(inv => SyntaxParsingHelpers.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var rarity = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "SetRarity")
          .Select(inv => SyntaxParsingHelpers.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var category = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "SetCategory")
          .Select(inv => TryReadCategoryNames(inv, 0))
          .FirstOrDefault(values => values.Count > 0);

        var entry = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = id };

        if (typeName != "Item")
          entry["type"] = typeName;

        if (stackSize.HasValue)
          entry["stackSize"] = stackSize.Value;

        if (sellValue.HasValue)
          entry["sellValue"] = sellValue.Value;

        if (hidden)
          entry["hidden"] = true;

        if (sweeping)
          entry["sweeping"] = true;

        if (targetLiquid)
          entry["targetLiquid"] = true;

        if (swingAnim.HasValue)
          entry["swingAnim"] = swingAnim.Value;

        if (!string.IsNullOrWhiteSpace(itemModel))
          entry["itemModel"] = itemModel;

        if (!string.IsNullOrWhiteSpace(itemTexture))
          entry["itemTexture"] = itemTexture;

        if (currencyAmount.HasValue)
          entry["currencyAmmount"] = currencyAmount.Value;

        if (rarity.HasValue)
          entry["rarity"] = rarity.Value;

        if (category is { Count: > 0 })
          entry["category"] = category;

        if (tags.Count > 0)
          entry["tags"] = tags;

        AddExtraConstructorFields(entry, ctor, constructorType, typeName, constructorParamsByType);

        list.Add(entry);
      }
    }

    return list;
  }

  private static void AddExtraConstructorFields(
    IDictionary<string, object?> entry,
    ObjectCreationExpressionSyntax ctor,
    string constructorType,
    string typeName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> constructorParamsByType
  )
  {
    if (typeName == "Item")
      return;

    var rawTypeName = constructorType.Split('.').Last().Trim();
    if (
      !constructorParamsByType.TryGetValue(rawTypeName, out var parameterNames)
      && !constructorParamsByType.TryGetValue(typeName, out parameterNames)
    )
    {
      return;
    }

    if (parameterNames.Count == 0)
      return;

    var args = ctor.ArgumentList?.Arguments;
    if (args is null || args.Value.Count <= 1)
      return;

    var positionalIndex = 0;
    var namedArgs = new Dictionary<string, ExpressionSyntax>(StringComparer.OrdinalIgnoreCase);
    var positionalArgs = new List<ExpressionSyntax>();

    foreach (var arg in args.Value)
    {
      if (arg.NameColon is not null)
      {
        namedArgs[arg.NameColon.Name.Identifier.Text] = arg.Expression;
        continue;
      }

      positionalArgs.Add(arg.Expression);
      positionalIndex++;
    }

    // Skip constructor arg 0 (id), then map remaining args to constructor parameter names.
    for (var parameterIndex = 1; parameterIndex < parameterNames.Count; parameterIndex++)
    {
      var parameterName = parameterNames[parameterIndex];
      ExpressionSyntax? expression = null;

      if (namedArgs.TryGetValue(parameterName, out var named))
      {
        expression = named;
      }
      else if (positionalArgs.Count > parameterIndex)
      {
        expression = positionalArgs[parameterIndex];
      }

      if (expression is null)
        continue;

      var value = TryParseLiteralLikeValue(expression);
      if (value is null)
        continue;

      entry[NormalizeConstructorFieldName(parameterName)] = value;
    }
  }

  private static string NormalizeConstructorFieldName(string parameterName)
  {
    if (parameterName.EndsWith("ID", StringComparison.Ordinal) && parameterName.Length > 2)
      return parameterName[..^2];
    return parameterName;
  }

  private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildConstructorParameterMap(string sourceRoot)
  {
    var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["Item"] = ["strID"] };

    var itemTypesRoot = Path.Combine(sourceRoot, ItemTypesRelativePath);
    if (!Directory.Exists(itemTypesRoot))
      return map;

    foreach (var file in Directory.EnumerateFiles(itemTypesRoot, "*.cs", SearchOption.AllDirectories))
    {
      var root = SyntaxParsingHelpers.ParseCompilationUnit(file);
      var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();

      foreach (var constructor in constructors)
      {
        var typeName = constructor.Identifier.Text;
        var parameterNames = constructor
          .ParameterList.Parameters.Select(parameter => parameter.Identifier.Text)
          .ToArray();

        if (parameterNames.Length == 0)
          continue;

        // Prefer the constructor with the most explicit parameters for richer metadata.
        if (!map.TryGetValue(typeName, out var existing) || parameterNames.Length > existing.Count)
          map[typeName] = parameterNames;
      }
    }

    return map;
  }

  private static string NormalizeTypeName(string constructorType)
  {
    var simpleName = constructorType.Split('.').Last().Trim();
    if (simpleName == "Item")
      return "Item";
    return simpleName.StartsWith("Item", StringComparison.Ordinal) ? simpleName[4..] : simpleName;
  }

  private static IReadOnlyList<string> TryReadCategoryNames(InvocationExpressionSyntax invocation, int argumentIndex)
  {
    if (invocation.ArgumentList.Arguments.Count <= argumentIndex)
      return [];

    var argument = invocation.ArgumentList.Arguments[argumentIndex].Expression;
    var names = argument
      .DescendantNodesAndSelf()
      .OfType<MemberAccessExpressionSyntax>()
      .Where(access => access.Expression.ToString() == "ItemCategory")
      .Select(access => access.Name.Identifier.Text)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
      .ToArray();

    return names;
  }

  private static object? TryReadExpressionArg(InvocationExpressionSyntax? invocation, int argumentIndex)
  {
    if (invocation is null || invocation.ArgumentList.Arguments.Count <= argumentIndex)
      return null;

    return TryParseLiteralLikeValue(invocation.ArgumentList.Arguments[argumentIndex].Expression);
  }

  private static object? TryParseLiteralLikeValue(ExpressionSyntax expression)
  {
    ExpressionSyntax cursor = expression;

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
          break;
      }

      break;
    }

    if (
      cursor is PrefixUnaryExpressionSyntax prefix
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

    return cursor switch
    {
      LiteralExpressionSyntax literal when literal.Token.Value is not null => literal.Token.Value,
      InvocationExpressionSyntax invocation when SyntaxParsingHelpers.GetInvocationName(invocation) == "nameof" =>
        invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression switch
        {
          IdentifierNameSyntax identifier => identifier.Identifier.Text,
          MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
          _ => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString(),
        },
      TypeOfExpressionSyntax typeOfExpression => typeOfExpression.Type.ToString(),
      MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      _ => null,
    };
  }
}
