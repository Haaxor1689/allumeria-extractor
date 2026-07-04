using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class GameVersionParser
{
  public static string? Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Game.cs");
    if (!File.Exists(path))
      return null;

    var root = SyntaxParsingHelpers.ParseCompilationUnit(path);
    var values = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var field in SyntaxParsingHelpers.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        if (variable.Initializer?.Value is not ExpressionSyntax initializer)
          continue;

        var value = TryEvaluateString(initializer, values);
        if (value is null)
          continue;

        values[variable.Identifier.Text] = value;
      }
    }

    return values.TryGetValue("FULL_VERSION", out var fullVersion) ? fullVersion : null;
  }

  private static string? TryEvaluateString(ExpressionSyntax expression, IReadOnlyDictionary<string, string> knownValues)
  {
    var reduced = Unwrap(expression);

    if (reduced is LiteralExpressionSyntax literal && literal.Token.Value is string literalText)
      return literalText;

    if (reduced is InterpolatedStringExpressionSyntax interpolated)
    {
      var parts = new List<string>();

      foreach (var content in interpolated.Contents)
      {
        if (content is InterpolatedStringTextSyntax text)
        {
          parts.Add(text.TextToken.ValueText);
          continue;
        }

        if (content is InterpolationSyntax interpolation)
        {
          var interpolationValue = TryResolveReference(interpolation.Expression, knownValues);
          if (interpolationValue is null)
            return null;

          parts.Add(interpolationValue);
        }
      }

      return string.Concat(parts);
    }

    return TryResolveReference(reduced, knownValues);
  }

  private static string? TryResolveReference(
    ExpressionSyntax expression,
    IReadOnlyDictionary<string, string> knownValues
  )
  {
    var reduced = Unwrap(expression);

    return reduced switch
    {
      IdentifierNameSyntax identifier => knownValues.TryGetValue(identifier.Identifier.Text, out var byName)
        ? byName
        : null,
      MemberAccessExpressionSyntax memberAccess => knownValues.TryGetValue(
        memberAccess.Name.Identifier.Text,
        out var byMember
      )
        ? byMember
        : null,
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
