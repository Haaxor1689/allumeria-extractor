using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class LootParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "LootTables", "LootDescription.cs");
    if (!File.Exists(path))
      return [];

    var root = SyntaxParsingHelpers.ParseCompilationUnit(path);
    var list = new List<object>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var field in SyntaxParsingHelpers.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var initializer = variable.Initializer?.Value;
        if (initializer is null)
          continue;

        var ctor = SyntaxParsingHelpers.TryGetRootObjectCreation(initializer);
        if (ctor is null || NormalizeTypeName(ctor.Type.ToString()) != "LootDescription")
          continue;

        var symbol = variable.Identifier.Text;
        var id = SyntaxParsingHelpers.TryReadIdFromObjectCreation(ctor) ?? symbol;
        if (!seen.Add(id))
          continue;

        var group = TryReadLootGroupName(ctor) ?? "Misc";
        var entryCount = SyntaxParsingHelpers
          .FindInvocations(initializer)
          .Count(invocation => SyntaxParsingHelpers.GetInvocationName(invocation) == "AddEntry");
        var entries = ParseEntriesFromInitializer(initializer);

        var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
          ["id"] = id,
          ["group"] = group,
          ["entryCount"] = entryCount,
          ["entries"] = entries,
        };

        list.Add(entry);
      }
    }

    return list;
  }

  private static string NormalizeTypeName(string typeName)
  {
    var text = typeName.Trim();

    if (text.StartsWith("global::", StringComparison.Ordinal))
      text = text["global::".Length..];

    var genericIndex = text.IndexOf('<');
    if (genericIndex > 0)
      text = text[..genericIndex];

    var lastDot = text.LastIndexOf('.');
    if (lastDot >= 0 && lastDot + 1 < text.Length)
      text = text[(lastDot + 1)..];

    return text.Trim();
  }

  private static string? TryReadLootGroupName(ObjectCreationExpressionSyntax ctor)
  {
    var args = ctor.ArgumentList?.Arguments;
    if (args is null || args.Value.Count < 2)
      return null;

    var expression = args.Value[1].Expression;
    return expression switch
    {
      MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      _ => null,
    };
  }

  private static IReadOnlyList<object> ParseEntriesFromInitializer(ExpressionSyntax initializer)
  {
    var args = CollectChainedAddEntryArguments(initializer);
    return ParseEntryArray(args);
  }

  private static IReadOnlyList<ExpressionSyntax> CollectChainedAddEntryArguments(ExpressionSyntax expression)
  {
    var cursor = Unwrap(expression);
    var reversed = new List<ExpressionSyntax>();

    while (cursor is InvocationExpressionSyntax invocation && IsAddEntryInvocation(invocation))
    {
      if (invocation.ArgumentList.Arguments.Count == 1)
        reversed.Add(invocation.ArgumentList.Arguments[0].Expression);

      if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        break;

      cursor = Unwrap(memberAccess.Expression);
    }

    reversed.Reverse();
    return reversed;
  }

  private static ExpressionSyntax GetChainedRootExpression(ExpressionSyntax expression)
  {
    var cursor = Unwrap(expression);

    while (cursor is InvocationExpressionSyntax invocation && IsAddEntryInvocation(invocation))
    {
      if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        break;

      cursor = Unwrap(memberAccess.Expression);
    }

    return cursor;
  }

  private static bool IsAddEntryInvocation(InvocationExpressionSyntax invocation)
  {
    return SyntaxParsingHelpers.GetInvocationName(invocation) == "AddEntry";
  }

  private static IReadOnlyList<object> ParseEntryArray(IEnumerable<ExpressionSyntax> expressions)
  {
    var list = new List<object>();

    foreach (var expression in expressions)
    {
      var parsed = ParseEntryExpression(expression);
      if (parsed is not null)
      {
        if (parsed is IReadOnlyList<object> inlineEntries)
        {
          list.AddRange(inlineEntries);
          continue;
        }

        list.Add(parsed);
      }
    }

    return list;
  }

  private static object? ParseEntryExpression(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);

    if (reduced is InvocationExpressionSyntax invocation && IsAddEntryInvocation(invocation))
      return ParseChainedEntry(invocation);

    if (reduced is ObjectCreationExpressionSyntax creation)
      return ParseObjectCreationEntry(creation);

    if (TryReadReferenceId(reduced, out var referenceId))
      return new Dictionary<string, object?>(StringComparer.Ordinal) { ["ref"] = referenceId };

    return null;
  }

  private static object? ParseChainedEntry(InvocationExpressionSyntax invocation)
  {
    var root = GetChainedRootExpression(invocation);
    var nestedArgs = CollectChainedAddEntryArguments(invocation);
    var nestedEntries = ParseEntryArray(nestedArgs);

    if (root is ObjectCreationExpressionSyntax creation)
    {
      var typeName = NormalizeTypeName(creation.Type.ToString());

      switch (typeName)
      {
        case "LootChance":
          return new Dictionary<string, object?>(StringComparer.Ordinal)
          {
            ["chance"] = TryReadFloatArg(creation, 0),
            ["entries"] = nestedEntries,
          };
        case "LootChooseExclusive":
          return new Dictionary<string, object?>(StringComparer.Ordinal) { ["oneOf"] = nestedEntries };
        case "LootPerPlayer":
          return new Dictionary<string, object?>(StringComparer.Ordinal)
          {
            ["perPlayer"] = true,
            ["entries"] = nestedEntries,
          };
        case "LootRequireItemTag":
          return new Dictionary<string, object?>(StringComparer.Ordinal)
          {
            ["needs"] = TryReadTagArg(creation, 0),
            ["entries"] = nestedEntries,
          };
        case "LootEntry":
          return nestedEntries;
      }
    }

    if (TryReadReferenceId(root, out var referenceId))
      return new Dictionary<string, object?>(StringComparer.Ordinal) { ["ref"] = referenceId };

    return null;
  }

  private static object? ParseObjectCreationEntry(ObjectCreationExpressionSyntax creation)
  {
    var typeName = NormalizeTypeName(creation.Type.ToString());

    return typeName switch
    {
      "LootFixedItem" => new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        ["item"] = TryReadItemArg(creation, 0),
        ["amount"] = TryReadIntArg(creation, 1),
      },
      "LootRandomAmount" => new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        ["item"] = TryReadItemArg(creation, 0),
        ["min"] = TryReadIntArg(creation, 1),
        ["max"] = TryReadIntArg(creation, 2),
      },
      "LootChance" => new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        ["chance"] = TryReadFloatArg(creation, 0),
        ["entries"] = Array.Empty<object>(),
      },
      "LootChooseExclusive" => new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        ["oneOf"] = Array.Empty<object>(),
      },
      "LootPerPlayer" => new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        ["perPlayer"] = true,
        ["entries"] = Array.Empty<object>(),
      },
      "LootRequireItemTag" => new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        ["needs"] = TryReadTagArg(creation, 0),
        ["entries"] = Array.Empty<object>(),
      },
      "LootEntry" => new Dictionary<string, object?>(StringComparer.Ordinal) { ["entries"] = Array.Empty<object>() },
      _ => null,
    };
  }

  private static bool TryReadReferenceId(ExpressionSyntax expression, out string? id)
  {
    var reduced = Unwrap(expression);

    switch (reduced)
    {
      case IdentifierNameSyntax identifier:
        id = identifier.Identifier.Text;
        return true;
      case MemberAccessExpressionSyntax memberAccess:
        id = memberAccess.Name.Identifier.Text;
        return true;
      default:
        id = null;
        return false;
    }
  }

  private static string? TryReadItemArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var argument = TryReadCtorArgExpression(creation, argumentIndex);
    if (argument is null)
      return null;

    return NormalizeEntityExpression(argument.ToString().Trim());
  }

  private static string? TryReadTagArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var argument = TryReadCtorArgExpression(creation, argumentIndex);
    var reduced = argument is null ? null : Unwrap(argument);

    return reduced switch
    {
      MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      _ => null,
    };
  }

  private static int? TryReadIntArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var argument = TryReadCtorArgExpression(creation, argumentIndex);
    return argument is null ? null : TryConvertToInt(TryParseLiteralLikeValue(argument));
  }

  private static float? TryReadFloatArg(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var argument = TryReadCtorArgExpression(creation, argumentIndex);
    return argument is null ? null : TryConvertToFloat(TryParseLiteralLikeValue(argument));
  }

  private static ExpressionSyntax? TryReadCtorArgExpression(ObjectCreationExpressionSyntax creation, int argumentIndex)
  {
    var args = creation.ArgumentList?.Arguments;
    if (args is null || args.Value.Count <= argumentIndex)
      return null;

    return args.Value[argumentIndex].Expression;
  }

  private static object? TryParseLiteralLikeValue(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);

    if (
      reduced is PrefixUnaryExpressionSyntax prefix
      && prefix.OperatorToken.Text == "-"
      && TryParseLiteralLikeValue(prefix.Operand) is { } nested
    )
    {
      return nested switch
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
      InvocationExpressionSyntax invocation when SyntaxParsingHelpers.GetInvocationName(invocation) == "nameof" =>
        invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression switch
        {
          IdentifierNameSyntax identifier => identifier.Identifier.Text,
          MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
          _ => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString(),
        },
      MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      _ => null,
    };
  }

  private static int? TryConvertToInt(object? value)
  {
    return value switch
    {
      byte v => v,
      sbyte v => v,
      short v => v,
      ushort v => v,
      int v => v,
      uint v when v <= int.MaxValue => (int)v,
      long v when v >= int.MinValue && v <= int.MaxValue => (int)v,
      ulong v when v <= int.MaxValue => (int)v,
      _ => null,
    };
  }

  private static float? TryConvertToFloat(object? value)
  {
    return value switch
    {
      byte v => v,
      sbyte v => v,
      short v => v,
      ushort v => v,
      int v => v,
      uint v => v,
      long v => v,
      ulong v => v,
      float v => v,
      double v => (float)v,
      decimal v => (float)v,
      _ => null,
    };
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
