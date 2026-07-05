using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class EffectParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "EntitySystem", "Effects", "Effect.cs");
    if (!File.Exists(path))
      return [];

    var root = SyntaxParsingUtils.ParseCompilationUnit(path);
    var list = new List<object>();
    var constructorParamsByType = BuildConstructorParameterMap(sourceRoot);

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

        if (!ctor.Type.ToString().StartsWith("Effect", StringComparison.Ordinal))
          continue;

        var symbol = variable.Identifier.Text;
        var constructorType = ctor.Type.ToString();
        var typeName = NormalizeTypeName(constructorType);
        var id = TryReadStringArg(ctor, 1) ?? symbol;
        var effectType = TryReadEffectTypeArg(ctor, 4);
        var intId = TryReadIntArg(ctor, 0);
        var textureX = TryReadIntArg(ctor, 2);
        var textureY = TryReadIntArg(ctor, 3);

        var entry = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = id };

        if (intId.HasValue)
          entry["intId"] = intId.Value;

        if (typeName != "Effect")
          entry["type"] = typeName;

        if (!string.IsNullOrWhiteSpace(effectType))
          entry["effectType"] = effectType;

        if (textureX.HasValue)
          entry["textureX"] = textureX.Value;

        if (textureY.HasValue)
          entry["textureY"] = textureY.Value;

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
    if (typeName == "Effect")
      return;

    var rawTypeName = constructorType.Split('.').Last().Trim();
    if (
      !constructorParamsByType.TryGetValue(rawTypeName, out var parameterNames)
      && !constructorParamsByType.TryGetValue(typeName, out parameterNames)
    )
    {
      return;
    }

    if (parameterNames.Count <= 5)
      return;

    var args = ctor.ArgumentList?.Arguments;
    if (args is null || args.Value.Count <= 5)
      return;

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
    }

    // Skip base effect constructor args (intID, strID, textureX, textureY, type).
    for (var parameterIndex = 5; parameterIndex < parameterNames.Count; parameterIndex++)
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
    var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
      ["Effect"] = ["intID", "strID", "textureX", "textureY", "type"],
    };

    var effectsRoot = Path.Combine(sourceRoot, "EntitySystem", "Effects");
    if (!Directory.Exists(effectsRoot))
      return map;

    foreach (var file in Directory.EnumerateFiles(effectsRoot, "*.cs", SearchOption.TopDirectoryOnly))
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);
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

  private static string? TryReadEffectTypeArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var expression = TryReadCtorArgExpression(creation, argumentIndex);
    if (expression is null)
      return null;

    var reduced = Unwrap(expression);

    return reduced switch
    {
      MemberAccessExpressionSyntax memberAccess
        when memberAccess.Expression.ToString().EndsWith("EffectType", StringComparison.Ordinal) => memberAccess
        .Name
        .Identifier
        .Text,
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      _ => null,
    };
  }

  private static ExpressionSyntax? TryReadCtorArgExpression(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var args = creation.ArgumentList?.Arguments;
    if (args is null || args.Value.Count <= argumentIndex)
      return null;

    return args.Value[argumentIndex].Expression;
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
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
      _ => null,
    };
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

  private static string NormalizeTypeName(string constructorType)
  {
    var simpleName = constructorType.Split('.').Last().Trim();
    if (simpleName == "Effect")
      return "Effect";
    return simpleName.StartsWith("Effect", StringComparison.Ordinal) ? simpleName[6..] : simpleName;
  }
}
