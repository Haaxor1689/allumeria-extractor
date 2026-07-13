using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class StructureParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var classes = new Dictionary<string, BuilderClassInfo>(StringComparer.Ordinal);

    foreach (var file in SyntaxParsingUtils.EnumerateSourceFiles(sourceRoot))
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);

      foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
      {
        var className = declaration.Identifier.Text;
        if (string.IsNullOrWhiteSpace(className))
          continue;

        var baseTypeNames =
          declaration.BaseList?.Types.Select(type => NormalizeTypeName(type.Type.ToString())).ToList() ?? [];

        MethodDeclarationSyntax? runMarkerCommand = null;
        foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
        {
          if (method.Identifier.Text == "RunMarkerCommand")
          {
            runMarkerCommand = method;
            break;
          }
        }

        classes[className] = new BuilderClassInfo(baseTypeNames, runMarkerCommand);
      }
    }

    var derivedBuilderNames = ResolveDerivedBuilderNames(classes);
    var result = new List<object>();

    foreach (var className in derivedBuilderNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
    {
      classes.TryGetValue(className, out var classInfo);

      var chests = classInfo?.RunMarkerCommand is null ? [] : ReadChestLootIds(classInfo.RunMarkerCommand);

      result.Add(
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
          ["id"] = TrimBuilderSuffix(className),
          ["chests"] = chests,
        }
      );
    }

    return result;
  }

  private static HashSet<string> ResolveDerivedBuilderNames(IReadOnlyDictionary<string, BuilderClassInfo> classes)
  {
    var derived = new HashSet<string>(StringComparer.Ordinal);
    var pending = new Queue<string>();
    pending.Enqueue("StructureBuilder");

    while (pending.Count > 0)
    {
      var currentBase = pending.Dequeue();

      foreach (var pair in classes)
      {
        if (derived.Contains(pair.Key))
          continue;

        if (!pair.Value.BaseTypeNames.Any(baseType => string.Equals(baseType, currentBase, StringComparison.Ordinal)))
          continue;

        derived.Add(pair.Key);
        pending.Enqueue(pair.Key);
      }
    }

    return derived;
  }

  private static List<object> ReadChestLootIds(MethodDeclarationSyntax runMarkerCommand)
  {
    var chests = new List<object>();

    foreach (var invocation in runMarkerCommand.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
      if (SyntaxParsingUtils.GetInvocationName(invocation) != "PlaceChest")
        continue;

      if (invocation.ArgumentList.Arguments.Count < 5)
        continue;

      var chestId = TryReadBlockMember(invocation.ArgumentList.Arguments[0].Expression);
      var lootId = TryReadLootDescriptionMember(invocation.ArgumentList.Arguments[4].Expression);
      if (string.IsNullOrWhiteSpace(chestId) || string.IsNullOrWhiteSpace(lootId))
        continue;

      var type = TryReadSwitchCaseLabel(invocation);

      var chestEntry = new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        ["chest"] = chestId,
        ["loot"] = lootId,
      };

      if (!string.IsNullOrWhiteSpace(type))
        chestEntry["type"] = type;

      chests.Add(chestEntry);
    }

    return chests;
  }

  private static string? TryReadBlockMember(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);
    if (
      reduced is MemberAccessExpressionSyntax memberAccess
      && NormalizeTypeName(memberAccess.Expression.ToString()) == "Block"
    )
      return memberAccess.Name.Identifier.Text;

    return null;
  }

  private static string? TryReadLootDescriptionMember(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);
    if (
      reduced is MemberAccessExpressionSyntax memberAccess
      && NormalizeTypeName(memberAccess.Expression.ToString()) == "LootDescription"
    )
    {
      return memberAccess.Name.Identifier.Text;
    }

    return null;
  }

  private static string? TryReadSwitchCaseLabel(InvocationExpressionSyntax invocation)
  {
    var section = invocation.Ancestors().OfType<SwitchSectionSyntax>().FirstOrDefault();
    if (section is null)
      return null;

    foreach (var label in section.Labels)
    {
      if (label is not CaseSwitchLabelSyntax caseLabel)
        continue;

      var reduced = Unwrap(caseLabel.Value);
      switch (reduced)
      {
        case LiteralExpressionSyntax literal:
          return literal.Token.ValueText;
        case IdentifierNameSyntax identifier:
          return identifier.Identifier.Text;
        case MemberAccessExpressionSyntax memberAccess:
          return memberAccess.Name.Identifier.Text;
        default:
          return reduced.ToString().Trim();
      }
    }

    return null;
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

  private static string TrimBuilderSuffix(string className)
  {
    const string suffix = "Builder";
    return className.EndsWith(suffix, StringComparison.Ordinal) ? className[..^suffix.Length] : className;
  }

  private sealed record BuilderClassInfo(
    IReadOnlyList<string> BaseTypeNames,
    MethodDeclarationSyntax? RunMarkerCommand
  );
}
