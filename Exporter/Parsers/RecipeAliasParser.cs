using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class RecipeAliasParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "Crafting", "RecipeAlias.cs");
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

        var creation = SyntaxParsingUtils.TryGetRootObjectCreation(initializer);
        if (creation is null || !creation.Type.ToString().EndsWith("RecipeAlias", StringComparison.Ordinal))
          continue;

        var id = SyntaxParsingUtils.TryReadIdFromObjectCreation(creation);
        if (string.IsNullOrWhiteSpace(id))
          id = variable.Identifier.Text;

        if (string.IsNullOrWhiteSpace(id))
          continue;

        var aliasEntries = SyntaxParsingUtils
          .FindInvocations(initializer)
          .Where(invocation => SyntaxParsingUtils.GetInvocationName(invocation) == "AddItem")
          .Select(invocation => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression)
          .Where(expression => expression is not null)
          .Select(expression => NormalizeItemExpression(expression!.ToString()))
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .ToList();

        aliasEntries.Reverse();
        aliasEntries = aliasEntries.Distinct(StringComparer.Ordinal).ToList();

        entries.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
          ["id"] = id,
          ["entries"] = aliasEntries,
        });
      }
    }

    return entries;
  }

  private static string? NormalizeItemExpression(string expression)
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

    if (
      text.StartsWith("Block.", StringComparison.Ordinal)
      && text.EndsWith(".item", StringComparison.Ordinal)
      && text.Length > "Block..item".Length
    )
      return text["Block.".Length..^".item".Length];

    return text;
  }
}
