using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class RecipeParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var list = new List<object>();
    var seenLocations = new HashSet<string>(StringComparer.Ordinal);

    foreach (var file in SyntaxParsingUtils.EnumerateSourceFiles(sourceRoot))
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);

      foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
      {
        if (!IsCraftingRecipeType(creation.Type.ToString()))
          continue;

        var key = $"{file}:{creation.SpanStart}:{creation.Span.Length}";
        if (!seenLocations.Add(key))
          continue;

        var expression = GetOutermostChainedExpression(creation);
        var parsed = ParseRecipeExpression(expression);

        if (parsed.TryGetValue("result", out var result) && result is not null)
          list.Add(parsed);
      }
    }

    return list;
  }

  private static Dictionary<string, object?> ParseRecipeExpression(ExpressionSyntax expression)
  {
    var invocations = SyntaxParsingUtils.FindInvocations(expression).ToArray();
    var recipeCreation = expression
      .DescendantNodesAndSelf()
      .OfType<ObjectCreationExpressionSyntax>()
      .FirstOrDefault(node => node.Type.ToString().EndsWith("CraftingRecipe", StringComparison.Ordinal));

    var itemStackCreation = recipeCreation
      ?.ArgumentList?.Arguments.Select(argument => SyntaxParsingUtils.TryGetRootObjectCreation(argument.Expression))
      .FirstOrDefault(creation =>
        creation is not null && creation.Type.ToString().EndsWith("ItemStack", StringComparison.Ordinal)
      );

    var resultExpression =
      itemStackCreation?.ArgumentList?.Arguments.Count > 0
        ? itemStackCreation.ArgumentList.Arguments[0].Expression.ToString().Trim()
        : null;

    var resultId = NormalizeEntityExpression(resultExpression);

    var resultAmount =
      itemStackCreation?.ArgumentList?.Arguments.Count > 1
        ? SyntaxParsingUtils.TryParseInt(itemStackCreation.ArgumentList.Arguments[1].Expression)
        : null;

    var station = recipeCreation
      ?.ArgumentList?.Arguments.Select(argument =>
        SyntaxParsingUtils.TryReadMemberName(argument.Expression, "CraftingStation")
      )
      .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    var requirements = invocations
      .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "AddReq")
      .Select(TryReadRequirement)
      .Where(req => req.HasValue)
      .Select(req => req!.Value)
      .ToList();

    var requirementMap = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var requirement in requirements)
    {
      var normalizedItem = NormalizeEntityExpression(requirement.ItemExpression);
      if (string.IsNullOrWhiteSpace(normalizedItem))
        continue;

      if (requirementMap.TryGetValue(normalizedItem, out var existing))
        requirementMap[normalizedItem] = existing + requirement.Amount;
      else
        requirementMap[normalizedItem] = requirement.Amount;
    }

    var entry = new Dictionary<string, object?>(StringComparer.Ordinal);

    if (!string.IsNullOrWhiteSpace(resultId))
      entry["result"] = resultId;

    if (resultAmount.HasValue)
      entry["amount"] = resultAmount.Value;

    if (!string.IsNullOrWhiteSpace(station))
      entry["station"] = station;

    if (requirementMap.Count > 0)
      entry["requirements"] = requirementMap;

    return entry;
  }

  private static bool IsCraftingRecipeType(string typeName)
  {
    return typeName.EndsWith("CraftingRecipe", StringComparison.Ordinal);
  }

  private static ExpressionSyntax GetOutermostChainedExpression(ExpressionSyntax expression)
  {
    var cursor = Unwrap(expression);

    while (
      cursor.Parent is MemberAccessExpressionSyntax memberAccess
      && memberAccess.Expression == cursor
      && memberAccess.Parent is InvocationExpressionSyntax invocation
      && invocation.Expression == memberAccess
    )
    {
      cursor = invocation;
    }

    return cursor;
  }

  private static (string ItemExpression, int Amount)? TryReadRequirement(InvocationExpressionSyntax invocation)
  {
    var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
    if (firstArg?.Expression is not ObjectCreationExpressionSyntax creation)
      return null;

    if (!creation.Type.ToString().EndsWith("RecipeEntry", StringComparison.Ordinal))
      return null;

    var args = creation.ArgumentList?.Arguments;
    if (args is null || args.Value.Count < 2)
      return null;

    var amount = SyntaxParsingUtils.TryParseInt(args.Value[1].Expression);
    if (!amount.HasValue)
      return null;

    return (args.Value[0].Expression.ToString().Trim(), amount.Value);
  }

  private static string? NormalizeEntityExpression(string? expression)
  {
    if (string.IsNullOrWhiteSpace(expression))
      return null;

    var text = expression.Trim();

    while (text.StartsWith("(", StringComparison.Ordinal) && text.Contains(')'))
    {
      var closeIndex = text.IndexOf(')');
      if (closeIndex <= 0)
        break;
      text = text[(closeIndex + 1)..].TrimStart();
    }

    if (text.StartsWith("Item.", StringComparison.Ordinal) && text.Length > "Item.".Length)
      return text["Item.".Length..];

    if (text.StartsWith("RecipeAlias.", StringComparison.Ordinal) && text.Length > "RecipeAlias.".Length)
      return text["RecipeAlias.".Length..];

    if (
      text.StartsWith("Block.", StringComparison.Ordinal)
      && text.EndsWith(".item", StringComparison.Ordinal)
      && text.Length > "Block..item".Length
    )
      return text["Block.".Length..^".item".Length];

    return text;
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
