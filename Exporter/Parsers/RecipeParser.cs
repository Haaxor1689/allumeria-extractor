using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class RecipeParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "Crafting", "CraftingRecipe.cs");
    if (!File.Exists(path))
      return [];

    var root = SyntaxParsingHelpers.ParseCompilationUnit(path);
    var list = new List<object>();

    foreach (var field in SyntaxParsingHelpers.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var expression = variable.Initializer?.Value;
        if (expression is null || !ContainsCraftingRecipeCreation(expression))
          continue;

        list.Add(ParseRecipeExpression(expression));
      }
    }

    foreach (
      var statement in root.DescendantNodes()
        .OfType<MethodDeclarationSyntax>()
        .Where(method => method.Identifier.Text == "InitCraftingRecipes")
        .SelectMany(method =>
          method.Body?.Statements.OfType<ExpressionStatementSyntax>() ?? Enumerable.Empty<ExpressionStatementSyntax>()
        )
    )
    {
      var expression = statement.Expression;
      if (!ContainsCraftingRecipeCreation(expression))
        continue;

      list.Add(ParseRecipeExpression(expression));
    }

    return list;
  }

  private static object ParseRecipeExpression(ExpressionSyntax expression)
  {
    var invocations = SyntaxParsingHelpers.FindInvocations(expression).ToArray();
    var recipeCreation = expression
      .DescendantNodesAndSelf()
      .OfType<ObjectCreationExpressionSyntax>()
      .FirstOrDefault(node => node.Type.ToString().EndsWith("CraftingRecipe", StringComparison.Ordinal));

    var itemStackCreation = recipeCreation
      ?.ArgumentList?.Arguments.Select(argument => SyntaxParsingHelpers.TryGetRootObjectCreation(argument.Expression))
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
        ? SyntaxParsingHelpers.TryParseInt(itemStackCreation.ArgumentList.Arguments[1].Expression)
        : null;

    var station = recipeCreation
      ?.ArgumentList?.Arguments.Select(argument =>
        SyntaxParsingHelpers.TryReadMemberName(argument.Expression, "CraftingStation")
      )
      .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    var requirements = invocations
      .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "AddReq")
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

  private static bool ContainsCraftingRecipeCreation(ExpressionSyntax expression)
  {
    return expression
      .DescendantNodesAndSelf()
      .OfType<ObjectCreationExpressionSyntax>()
      .Any(node => node.Type.ToString().EndsWith("CraftingRecipe", StringComparison.Ordinal));
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

    var amount = SyntaxParsingHelpers.TryParseInt(args.Value[1].Expression);
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
}
