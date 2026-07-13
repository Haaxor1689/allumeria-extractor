using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class BlockMaterialParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Blocks", "Blocks", "BlockMaterial.cs");
    if (!File.Exists(path))
      return [];

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
        if (ctor is null || ctor.Type.ToString() != "BlockMaterial")
          continue;

        var id = variable.Identifier.Text;
        var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
          ["id"] = id,
          ["miningLevel"] = TryReadIntArg(ctor, 0) ?? 0,
          ["punchesRequired"] = TryReadIntArg(ctor, 1) ?? 0,
          ["overcharge"] = TryReadIntArg(ctor, 2) ?? 0,
          ["swingMultiplier"] = TryReadFloatArg(ctor, 4) ?? 0f,
          ["soundId"] = TryReadStringArg(ctor, 5) ?? string.Empty,
          ["canBeBlownUp"] = TryReadBoolArg(ctor, 6) ?? true,
          ["hammerLevel"] = TryReadIntArg(ctor, 7) ?? 1,
        };

        var preferredTool = TryReadPreferredToolArg(ctor, 3);
        if (!string.IsNullOrWhiteSpace(preferredTool))
          entry["preferredTool"] = preferredTool;

        entries.Add(entry);
      }
    }

    return entries;
  }

  private static bool? TryReadBoolArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var expression = TryReadCtorArgExpression(creation, argumentIndex);
    if (expression is null)
      return null;

    return Unwrap(expression) switch
    {
      LiteralExpressionSyntax literal when literal.Token.Value is bool value => value,
      _ => null,
    };
  }

  private static float? TryReadFloatArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var expression = TryReadCtorArgExpression(creation, argumentIndex);
    if (expression is null)
      return null;

    return TryParseNumericLiteralLikeValue(expression) switch
    {
      int intValue => intValue,
      long longValue => longValue,
      float floatValue => floatValue,
      double doubleValue => (float)doubleValue,
      decimal decimalValue => (float)decimalValue,
      _ => null,
    };
  }

  private static int? TryReadIntArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var expression = TryReadCtorArgExpression(creation, argumentIndex);
    if (expression is null)
      return null;

    return TryParseNumericLiteralLikeValue(expression) switch
    {
      int intValue => intValue,
      long longValue when longValue >= int.MinValue && longValue <= int.MaxValue => (int)longValue,
      float floatValue => (int)floatValue,
      double doubleValue => (int)doubleValue,
      decimal decimalValue => (int)decimalValue,
      _ => null,
    };
  }

  private static string? TryReadPreferredToolArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var expression = TryReadCtorArgExpression(creation, argumentIndex);
    if (expression is null)
      return null;

    var reduced = Unwrap(expression);
    if (reduced.Kind() == SyntaxKind.NullLiteralExpression)
      return null;

    return SyntaxParsingUtils.TryReadMemberName(reduced, "ItemTag");
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
      6 => args.Value.FirstOrDefault(arg => arg.NameColon?.Name.Identifier.Text == "canBeBlownUp")?.Expression,
      7 => args.Value.FirstOrDefault(arg => arg.NameColon?.Name.Identifier.Text == "hammerLevel")?.Expression,
      _ => null,
    };

    if (namedExpression is not null)
      return namedExpression;

    if (args.Value.Count > argumentIndex)
      return args.Value[argumentIndex].Expression;

    return null;
  }

  private static object? TryParseLiteralLikeValue(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);

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

  private static object? TryParseNumericLiteralLikeValue(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);

    if (
      reduced is PrefixUnaryExpressionSyntax prefix
      && prefix.OperatorToken.Kind() == SyntaxKind.MinusToken
      && TryParseNumericLiteralLikeValue(prefix.Operand) is { } negativeValue
    )
    {
      return negativeValue switch
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
}