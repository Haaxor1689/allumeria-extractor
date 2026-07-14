using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class CatalogueParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "Crafting", "Catalogue.cs");
    if (!File.Exists(path))
      return [];

    var root = SyntaxParsingUtils.ParseCompilationUnit(path);
    var itemIdsBySymbol = ReadItemIdsBySymbol(sourceRoot);
    var catalogues = new List<object>();

    foreach (var field in SyntaxParsingUtils.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var initializer = variable.Initializer?.Value;
        if (initializer is null)
          continue;

        var ctor = SyntaxParsingUtils.TryGetRootObjectCreation(initializer);
        if (ctor is null || NormalizeTypeName(ctor.Type.ToString()) != "Catalogue")
          continue;

        var entries = ParseEntries(initializer, itemIdsBySymbol);
        catalogues.Add(
          new Dictionary<string, object?>(StringComparer.Ordinal)
          {
            ["id"] = variable.Identifier.Text,
            ["entries"] = entries,
          }
        );
      }
    }

    return catalogues;
  }

  private static object[] ParseEntries(
    ExpressionSyntax initializer,
    IReadOnlyDictionary<string, string> itemIdsBySymbol
  )
  {
    var entries = SyntaxParsingUtils
      .FindInvocations(initializer)
      .Where(invocation => SyntaxParsingUtils.GetInvocationName(invocation) == "AddEntry")
      .Select(invocation => ParseEntry(invocation, itemIdsBySymbol))
      .Where(entry => entry is not null)
      .Cast<object>()
      .ToArray();

    Array.Reverse(entries);
    return entries;
  }

  private static Dictionary<string, object?>? ParseEntry(
    InvocationExpressionSyntax invocation,
    IReadOnlyDictionary<string, string> itemIdsBySymbol
  )
  {
    var entryExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    var creation = SyntaxParsingUtils.TryGetRootObjectCreation(entryExpression!);
    if (creation is null || NormalizeTypeName(creation.Type.ToString()) != "ShopEntry")
      return null;

    var arguments = creation.ArgumentList?.Arguments;
    if (arguments is null || arguments.Value.Count < 3)
      return null;

    var item = NormalizeEntityExpression(arguments.Value[0].Expression.ToString(), itemIdsBySymbol);
    var amount = SyntaxParsingUtils.TryParseInt(arguments.Value[1].Expression);
    var price = SyntaxParsingUtils.TryParseInt(arguments.Value[2].Expression);

    if (string.IsNullOrWhiteSpace(item) || !amount.HasValue || !price.HasValue)
      return null;

    return new Dictionary<string, object?>(StringComparer.Ordinal)
    {
      ["item"] = item,
      ["amount"] = amount.Value,
      ["price"] = price.Value,
    };
  }

  private static Dictionary<string, string> ReadItemIdsBySymbol(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "Item.cs");
    if (!File.Exists(path))
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var root = SyntaxParsingUtils.ParseCompilationUnit(path);
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        var symbol = variable.Identifier.Text;
        var id = SyntaxParsingUtils.TryReadIdFromObjectCreation(ctor) ?? symbol;
        map[symbol] = id;
      }
    }

    return map;
  }

  private static string? NormalizeEntityExpression(
    string? expression,
    IReadOnlyDictionary<string, string> itemIdsBySymbol
  )
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
    {
      var itemSymbol = text["Item.".Length..];
      return itemIdsBySymbol.TryGetValue(itemSymbol, out var itemId) ? itemId : itemSymbol;
    }

    if (
      text.StartsWith("Block.", StringComparison.Ordinal)
      && text.EndsWith(".item", StringComparison.Ordinal)
      && text.Length > "Block..item".Length
    )
    {
      return text["Block.".Length..^".item".Length];
    }

    return text;
  }

  private static string NormalizeTypeName(string typeName)
  {
    var simpleName = typeName.Split('.').Last().Trim();
    return simpleName;
  }
}
