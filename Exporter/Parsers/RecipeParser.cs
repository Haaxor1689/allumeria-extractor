using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class RecipeParser
{
  private sealed class RequirementResolutionContext
  {
    public Dictionary<string, string> ItemIdsBySymbol { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> BlockIdsBySymbol { get; } = new(StringComparer.Ordinal);
  }

  public static List<object> Parse(string sourceRoot)
  {
    var list = new List<object>();
    var seenLocations = new HashSet<string>(StringComparer.Ordinal);
    var resolutionContext = BuildRequirementResolutionContext(sourceRoot);

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
        var parsed = ParseRecipeExpression(expression, resolutionContext);

        if (parsed.TryGetValue("result", out var result) && result is not null)
          list.Add(parsed);
      }
    }

    return list;
  }

  private static Dictionary<string, object?> ParseRecipeExpression(
    ExpressionSyntax expression,
    RequirementResolutionContext resolutionContext
  )
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
        ? itemStackCreation.ArgumentList.Arguments[0].Expression
        : null;

    var resultId = resultExpression is not null
      ? ResolveRequirementItemId(resultExpression, resolutionContext, expression)
      : null;

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
      var normalizedItem = ResolveRequirementItemId(requirement.ItemExpression, resolutionContext, expression);
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

  private static (ExpressionSyntax ItemExpression, int Amount)? TryReadRequirement(
    InvocationExpressionSyntax invocation
  )
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

    return (args.Value[0].Expression, amount.Value);
  }

  private static RequirementResolutionContext BuildRequirementResolutionContext(string sourceRoot)
  {
    var context = new RequirementResolutionContext();

    foreach (var file in SyntaxParsingUtils.EnumerateSourceFiles(sourceRoot))
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);

      foreach (var field in SyntaxParsingUtils.FindPublicStaticFields(root))
      {
        foreach (var variable in field.Declaration.Variables)
        {
          var initializer = variable.Initializer?.Value;
          if (initializer is null)
            continue;

          var creation = SyntaxParsingUtils.TryGetRootObjectCreation(initializer);
          if (creation is null)
            continue;

          var typeName = creation.Type.ToString().Split('.').Last().Trim();
          var id = SyntaxParsingUtils.TryReadIdFromObjectCreation(creation) ?? variable.Identifier.Text;
          if (string.IsNullOrWhiteSpace(id))
            continue;

          if (typeName.StartsWith("Item", StringComparison.Ordinal))
            context.ItemIdsBySymbol[variable.Identifier.Text] = id;

          if (typeName.StartsWith("Block", StringComparison.Ordinal))
            context.BlockIdsBySymbol[variable.Identifier.Text] = id;
        }
      }
    }

    return context;
  }

  private static string? ResolveRequirementItemId(
    ExpressionSyntax expression,
    RequirementResolutionContext resolutionContext,
    SyntaxNode scopeNode,
    HashSet<string>? visited = null
  )
  {
    visited ??= new HashSet<string>(StringComparer.Ordinal);

    var unwrapped = Unwrap(expression);

    if (unwrapped is MemberAccessExpressionSyntax memberAccess)
    {
      if (memberAccess.Expression is IdentifierNameSyntax owner)
      {
        if (owner.Identifier.Text == "Item")
        {
          var symbol = memberAccess.Name.Identifier.Text;
          if (resolutionContext.ItemIdsBySymbol.TryGetValue(symbol, out var itemId))
            return itemId;

          return symbol;
        }

        if (owner.Identifier.Text == "Block" && memberAccess.Name.Identifier.Text == "item")
          return null;
      }

      if (
        memberAccess.Name.Identifier.Text == "item"
        && memberAccess.Expression is MemberAccessExpressionSyntax blockMember
        && blockMember.Expression is IdentifierNameSyntax blockOwner
        && blockOwner.Identifier.Text == "Block"
      )
      {
        var blockSymbol = blockMember.Name.Identifier.Text;
        if (resolutionContext.BlockIdsBySymbol.TryGetValue(blockSymbol, out var blockId))
          return blockId;

        return blockSymbol;
      }

      if (memberAccess.Name.Identifier.Text == "item")
      {
        var nested = ResolveRequirementItemId(memberAccess.Expression, resolutionContext, scopeNode, visited);
        if (!string.IsNullOrWhiteSpace(nested))
          return nested;
      }
    }

    if (unwrapped is IdentifierNameSyntax identifier)
    {
      if (!visited.Add(identifier.Identifier.Text))
        return null;

      if (resolutionContext.ItemIdsBySymbol.TryGetValue(identifier.Identifier.Text, out var itemId))
        return itemId;

      if (resolutionContext.BlockIdsBySymbol.TryGetValue(identifier.Identifier.Text, out var blockId))
        return blockId;

      var initializer = TryFindNearestInitializer(scopeNode, identifier.Identifier.Text);
      if (initializer is not null)
      {
        var resolved = ResolveRequirementItemId(initializer, resolutionContext, scopeNode, visited);
        if (!string.IsNullOrWhiteSpace(resolved))
          return resolved;
      }

      return identifier.Identifier.Text;
    }

    if (unwrapped is ObjectCreationExpressionSyntax creation)
    {
      if (creation.Type.ToString().Split('.').Last().Trim().StartsWith("Item", StringComparison.Ordinal))
      {
        var createdId = SyntaxParsingUtils.TryReadIdFromObjectCreation(creation);
        if (!string.IsNullOrWhiteSpace(createdId))
          return createdId;
      }
    }

    return NormalizeEntityExpression(unwrapped.ToString().Trim());
  }

  private static ExpressionSyntax? TryFindNearestInitializer(SyntaxNode scopeNode, string identifier)
  {
    var method = scopeNode.AncestorsAndSelf().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
    if (method is not null)
    {
      var localInit = method
        .DescendantNodes()
        .OfType<VariableDeclaratorSyntax>()
        .Where(variable => variable.Identifier.Text == identifier)
        .Where(variable => variable.Initializer is not null && variable.SpanStart < scopeNode.SpanStart)
        .OrderByDescending(variable => variable.SpanStart)
        .Select(variable => variable.Initializer!.Value)
        .FirstOrDefault();

      if (localInit is not null)
        return localInit;
    }

    var containingType = scopeNode.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
    if (containingType is not null)
    {
      var fieldInit = containingType
        .DescendantNodes()
        .OfType<VariableDeclaratorSyntax>()
        .Where(variable => variable.Identifier.Text == identifier)
        .Where(variable => variable.Initializer is not null)
        .OrderByDescending(variable => variable.SpanStart)
        .Select(variable => variable.Initializer!.Value)
        .FirstOrDefault();

      if (fieldInit is not null)
        return fieldInit;
    }

    return null;
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
