using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ItemParser
{
  private const string ItemTypesRelativePath = "Items\\ItemTypes";
  private static readonly HashSet<string> ValidSlotTypes =
  [
    "Helmet",
    "Chestplate",
    "Greaves",
    "Trinket",
    "Ammo",
    "Currency",
  ];

  public static List<object> Parse(string sourceRoot, List<object> items)
  {
    var path = Path.Combine(sourceRoot, "Items", "Item.cs");
    if (!File.Exists(path))
      return items;

    var root = SyntaxParsingUtils.ParseCompilationUnit(path);
    var itemEntriesById = BuildEntryMap(items);
    var constructorParamsByType = BuildConstructorParameterMap(sourceRoot);
    var slotTypeResolversByType = BuildSlotTypeResolvers(sourceRoot);
    var ammoTypeNames = ReadAmmoTypeNames(sourceRoot);

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

        var invocations = SyntaxParsingUtils.FindInvocations(initializer).ToArray();
        var symbol = variable.Identifier.Text;
        var id = SyntaxParsingUtils.TryReadIdFromObjectCreation(ctor) ?? symbol;
        var constructorType = ctor.Type.ToString();
        var typeName = NormalizeTypeName(constructorType);

        var tags = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "AddTag")
          .Select(inv => new
          {
            key = SyntaxParsingUtils.TryReadQualifiedMemberArg(inv, 0, "ItemTag"),
            value = NormalizeTagValue(
              SyntaxParsingUtils.TryReadQualifiedMemberArg(inv, 0, "ItemTag"),
              TryReadExpressionArg(inv, 1) ?? true,
              ammoTypeNames
            ),
          })
          .Where(entry => !string.IsNullOrWhiteSpace(entry.key))
          .GroupBy(entry => entry.key!, StringComparer.OrdinalIgnoreCase)
          .Select(group => new KeyValuePair<string, object?>(group.Key, group.Last().value))
          .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
          .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var stackSize = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "SetStackSize")
          .Select(inv => SyntaxParsingUtils.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var sellValue = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "SellValue")
          .Select(inv => SyntaxParsingUtils.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var hidden = invocations.Any(inv => SyntaxParsingUtils.GetInvocationName(inv) == "Hide");
        var sweeping = invocations.Any(inv => SyntaxParsingUtils.GetInvocationName(inv) == "MakeSweeping");
        var targetLiquid = invocations.Any(inv => SyntaxParsingUtils.GetInvocationName(inv) == "TargetLiquid");

        var swingAnim = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) is "SetSwingAnim" or "SetSwingAnimation")
          .Select(inv => SyntaxParsingUtils.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var modelInvocation = invocations.FirstOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetModel"
        );
        var itemModel = TryReadExpressionArg(modelInvocation, 0) as string;
        var itemTexture = TryReadExpressionArg(modelInvocation, 1) as string;

        var currencyAmount = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "IsCurrency")
          .Select(inv => SyntaxParsingUtils.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var rarity = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "SetRarity")
          .Select(inv => SyntaxParsingUtils.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var category = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "SetCategory")
          .Select(inv => TryReadCategoryNames(inv, 0))
          .FirstOrDefault(values => values.Count > 0);

        var entry = itemEntriesById.TryGetValue(id, out var existingEntry)
          ? existingEntry
          : new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = id };

        if (typeName != "Item")
          entry["class"] = typeName;

        if (stackSize.HasValue)
          entry["stackSize"] = stackSize.Value;

        if (sellValue.HasValue)
          entry["sellValue"] = sellValue.Value;

        if (hidden)
          entry["hidden"] = true;

        if (sweeping)
          entry["sweeping"] = true;

        if (targetLiquid)
          entry["targetLiquid"] = true;

        if (swingAnim.HasValue)
          entry["swingAnim"] = swingAnim.Value;

        if (!string.IsNullOrWhiteSpace(itemModel))
          entry["itemModel"] = itemModel;

        if (!string.IsNullOrWhiteSpace(itemTexture))
          entry["itemTexture"] = itemTexture;

        if (currencyAmount.HasValue)
          entry["currencyAmmount"] = currencyAmount.Value;

        if (rarity.HasValue)
          entry["rarity"] = rarity.Value;

        if (category is { Count: > 0 })
          MergeCategories(entry, category);

        if (tags.Count > 0)
          entry["tags"] = tags;

        AddExtraConstructorFields(entry, ctor, constructorType, typeName, constructorParamsByType);

        var rawTypeName = constructorType.Split('.').Last().Trim();
        if (
          slotTypeResolversByType.TryGetValue(rawTypeName, out var slotTypeResolver)
          || slotTypeResolversByType.TryGetValue($"Item{typeName}", out slotTypeResolver)
          || slotTypeResolversByType.TryGetValue(typeName, out slotTypeResolver)
        )
        {
          var slotType = ResolveSlotTypeFromEntry(slotTypeResolver, entry);
          if (!string.IsNullOrWhiteSpace(slotType))
            entry["slotType"] = slotType;
        }

        if (!itemEntriesById.ContainsKey(id))
        {
          items.Add(entry);
          itemEntriesById[id] = entry;
        }
      }
    }

    ApplyAssignCategoriesLogic(itemEntriesById.Values);

    return items;
  }

  private static void ApplyAssignCategoriesLogic(IEnumerable<Dictionary<string, object?>> entries)
  {
    foreach (var entry in entries)
    {
      var hidden = entry.TryGetValue("hidden", out var hiddenValue) && hiddenValue is true;
      if (hidden)
        continue;

      var derivedCategories = new List<string> { };

      var hasBlock =
        entry.TryGetValue("block", out var blockValue) && !string.IsNullOrWhiteSpace(blockValue?.ToString());
      derivedCategories.Add(hasBlock ? "blocks" : "items");

      var hasMeleeDamage = HasTag(entry, "melee_damage");
      var hasRangedDamage = HasTag(entry, "ranged_damage");
      var hasAmmo = HasTag(entry, "ammo");
      if (hasMeleeDamage || hasRangedDamage || hasAmmo)
        derivedCategories.Add("weapons");

      var hasPickaxe = HasTag(entry, "pickaxe");
      var hasAxe = HasTag(entry, "axe");
      if (hasPickaxe || hasAxe)
        derivedCategories.Add("tools");

      MergeCategories(entry, derivedCategories);
    }
  }

  private static bool HasTag(IReadOnlyDictionary<string, object?> entry, string tagName)
  {
    if (!entry.TryGetValue("tags", out var tagsValue) || tagsValue is not IReadOnlyDictionary<string, object?> tags)
      return false;

    return tags.ContainsKey(tagName);
  }

  private static void MergeCategories(IDictionary<string, object?> entry, IEnumerable<string> categories)
  {
    var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (entry.TryGetValue("category", out var existingValue) && existingValue is IEnumerable<string> existingCategories)
    {
      foreach (var existing in existingCategories)
      {
        if (!string.IsNullOrWhiteSpace(existing))
          merged.Add(existing);
      }
    }

    foreach (var category in categories)
    {
      if (!string.IsNullOrWhiteSpace(category))
        merged.Add(category);
    }

    if (merged.Count == 0)
      return;

    entry["category"] = merged.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
  }

  private static Dictionary<string, Dictionary<string, object?>> BuildEntryMap(IEnumerable<object> items)
  {
    var map = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

    foreach (var item in items)
    {
      if (item is not Dictionary<string, object?> entry)
        continue;

      if (!entry.TryGetValue("id", out var idValue))
        continue;

      var id = idValue?.ToString();
      if (string.IsNullOrWhiteSpace(id))
        continue;

      map[id] = entry;
    }

    return map;
  }

  private static void AddExtraConstructorFields(
    IDictionary<string, object?> entry,
    ObjectCreationExpressionSyntax ctor,
    string constructorType,
    string typeName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> constructorParamsByType
  )
  {
    if (typeName == "Item")
      return;

    var rawTypeName = constructorType.Split('.').Last().Trim();
    if (
      !constructorParamsByType.TryGetValue(rawTypeName, out var parameterNames)
      && !constructorParamsByType.TryGetValue(typeName, out parameterNames)
    )
    {
      return;
    }

    if (parameterNames.Count == 0)
      return;

    var args = ctor.ArgumentList?.Arguments;
    if (args is null || args.Value.Count <= 1)
      return;

    var positionalIndex = 0;
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
      positionalIndex++;
    }

    // Skip constructor arg 0 (id), then map remaining args to constructor parameter names.
    for (var parameterIndex = 1; parameterIndex < parameterNames.Count; parameterIndex++)
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
    var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["Item"] = ["strID"] };

    var itemTypesRoot = Path.Combine(sourceRoot, ItemTypesRelativePath);
    if (!Directory.Exists(itemTypesRoot))
      return map;

    foreach (var file in Directory.EnumerateFiles(itemTypesRoot, "*.cs", SearchOption.AllDirectories))
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

  private static string NormalizeTypeName(string constructorType)
  {
    var simpleName = constructorType.Split('.').Last().Trim();
    if (simpleName == "Item")
      return "Item";
    return simpleName.StartsWith("Item", StringComparison.Ordinal) ? simpleName[4..] : simpleName;
  }

  private static IReadOnlyDictionary<string, SlotTypeResolver> BuildSlotTypeResolvers(string sourceRoot)
  {
    var map = new Dictionary<string, SlotTypeResolver>(StringComparer.OrdinalIgnoreCase);

    var itemTypesRoot = Path.Combine(sourceRoot, ItemTypesRelativePath);
    if (!Directory.Exists(itemTypesRoot))
      return map;

    foreach (var file in Directory.EnumerateFiles(itemTypesRoot, "*.cs", SearchOption.AllDirectories))
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);

      foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
      {
        var resolver = TryBuildSlotTypeResolver(type);
        if (resolver is null)
          continue;

        map[type.Identifier.Text] = resolver;
      }
    }

    return map;
  }

  private static SlotTypeResolver? TryBuildSlotTypeResolver(TypeDeclarationSyntax type)
  {
    var allowedInSlot = type.DescendantNodes()
      .OfType<MethodDeclarationSyntax>()
      .FirstOrDefault(method =>
        method.Identifier.Text == "AllowedInSlot"
        && method.ParameterList.Parameters.Count == 1
        && method.ReturnType.ToString() == "bool"
      );

    if (allowedInSlot is null)
      return null;

    var returnExpression = allowedInSlot switch
    {
      { ExpressionBody: not null } => allowedInSlot.ExpressionBody.Expression,
      { Body: not null } => allowedInSlot
        .Body.Statements.OfType<ReturnStatementSyntax>()
        .Select(statement => statement.Expression)
        .FirstOrDefault(expression => expression is not null),
      _ => null,
    };

    if (returnExpression is null)
      return null;

    var comparedOperands = CollectComparedOperands(returnExpression)
      .Select(operand => operand.ToString())
      .Where(text => !string.IsNullOrWhiteSpace(text))
      .ToArray();

    if (comparedOperands.Length == 0)
      return null;

    var explicitSlotTypes = comparedOperands
      .Select(TryReadSlotTypeName)
      .Where(name =>
        !string.IsNullOrWhiteSpace(name)
        && !string.Equals(name, "Normal", StringComparison.OrdinalIgnoreCase)
        && ValidSlotTypes.Contains(name!)
      )
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();

    if (explicitSlotTypes.Length == 1)
      return new SlotTypeResolver(
        explicitSlotTypes[0],
        null,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      );

    var slotFieldName = comparedOperands
      .Select(TryReadSlotFieldName)
      .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
    if (string.IsNullOrWhiteSpace(slotFieldName))
      return null;

    var enumToSlotMap = BuildEnumToSlotMapForField(type, slotFieldName!);
    return new SlotTypeResolver(null, slotFieldName, enumToSlotMap);
  }

  private static IEnumerable<ExpressionSyntax> CollectComparedOperands(ExpressionSyntax expression)
  {
    if (expression is BinaryExpressionSyntax binary)
    {
      if (binary.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression)
      {
        if (IsSlotTypeAccess(binary.Left))
          yield return binary.Right;
        else if (IsSlotTypeAccess(binary.Right))
          yield return binary.Left;

        yield break;
      }

      if (
        binary.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalOrExpression
        || binary.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression
      )
      {
        foreach (var nested in CollectComparedOperands(binary.Left))
          yield return nested;
        foreach (var nested in CollectComparedOperands(binary.Right))
          yield return nested;
      }
    }
  }

  private static bool IsSlotTypeAccess(ExpressionSyntax expression)
  {
    return expression.ToString().EndsWith(".slotType", StringComparison.Ordinal);
  }

  private static string? TryReadSlotTypeName(string expressionText)
  {
    const string prefix = "InventorySlot.SlotType.";
    if (!expressionText.StartsWith(prefix, StringComparison.Ordinal))
      return null;

    var name = expressionText[prefix.Length..];
    return string.IsNullOrWhiteSpace(name) ? null : name;
  }

  private static string? TryReadSlotFieldName(string expressionText)
  {
    if (expressionText.StartsWith("this.", StringComparison.Ordinal))
      return expressionText["this.".Length..];

    if (expressionText.Contains('.'))
      return null;

    return expressionText;
  }

  private static Dictionary<string, string> BuildEnumToSlotMapForField(TypeDeclarationSyntax type, string slotFieldName)
  {
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var constructor in type.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
    {
      foreach (var switchStatement in constructor.Body?.DescendantNodes().OfType<SwitchStatementSyntax>() ?? [])
      {
        foreach (var section in switchStatement.Sections)
        {
          var assignment = section
            .Statements.OfType<ExpressionStatementSyntax>()
            .Select(statement => statement.Expression)
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(IsTargetSlotField);

          if (assignment is null)
            continue;

          var slotTypeName = TryReadSlotTypeName(assignment.Right.ToString());
          if (string.IsNullOrWhiteSpace(slotTypeName) || !ValidSlotTypes.Contains(slotTypeName))
            continue;

          foreach (var label in section.Labels.OfType<CaseSwitchLabelSyntax>())
          {
            if (label.Value is not MemberAccessExpressionSyntax member)
              continue;

            var enumName = member.Name.Identifier.Text;
            if (!string.IsNullOrWhiteSpace(enumName))
              map[enumName] = slotTypeName;
          }
        }
      }
    }

    return map;

    bool IsTargetSlotField(AssignmentExpressionSyntax assignment)
    {
      if (assignment.Left is MemberAccessExpressionSyntax memberAccess)
        return memberAccess.Name.Identifier.Text == slotFieldName;

      if (assignment.Left is IdentifierNameSyntax identifier)
        return identifier.Identifier.Text == slotFieldName;

      return false;
    }
  }

  private static string? ResolveSlotTypeFromEntry(SlotTypeResolver resolver, IReadOnlyDictionary<string, object?> entry)
  {
    if (!string.IsNullOrWhiteSpace(resolver.DirectSlotType) && ValidSlotTypes.Contains(resolver.DirectSlotType!))
      return resolver.DirectSlotType;

    if (!string.IsNullOrWhiteSpace(resolver.SlotFieldName))
    {
      if (
        entry.TryGetValue(resolver.SlotFieldName!, out var directFieldValue)
        && directFieldValue is string directFieldName
        && ValidSlotTypes.Contains(directFieldName)
      )
      {
        return directFieldName;
      }

      foreach (var value in entry.Values.OfType<string>())
      {
        if (
          resolver.EnumToSlotMap.TryGetValue(value, out var mappedSlotType) && ValidSlotTypes.Contains(mappedSlotType)
        )
          return mappedSlotType;

        if (ValidSlotTypes.Contains(value))
          return value;
      }
    }

    return null;
  }

  private static IReadOnlyList<string> TryReadCategoryNames(InvocationExpressionSyntax invocation, int argumentIndex)
  {
    if (invocation.ArgumentList.Arguments.Count <= argumentIndex)
      return [];

    var argument = invocation.ArgumentList.Arguments[argumentIndex].Expression;
    var names = argument
      .DescendantNodesAndSelf()
      .OfType<MemberAccessExpressionSyntax>()
      .Where(access => access.Expression.ToString() == "ItemCategory")
      .Select(access => access.Name.Identifier.Text)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
      .ToArray();

    return names;
  }

  private static object? TryReadExpressionArg(InvocationExpressionSyntax? invocation, int argumentIndex)
  {
    if (invocation is null || invocation.ArgumentList.Arguments.Count <= argumentIndex)
      return null;

    return TryParseLiteralLikeValue(invocation.ArgumentList.Arguments[argumentIndex].Expression);
  }

  private static object? NormalizeTagValue(string? tagKey, object? value, IReadOnlyList<string> ammoTypeNames)
  {
    if (!string.Equals(tagKey, "ammo", StringComparison.OrdinalIgnoreCase) || value is null)
      return value;

    if (!TryReadInt(value, out var ammoIndex))
      return value;

    if (ammoIndex < 0 || ammoIndex >= ammoTypeNames.Count)
      return value;

    var ammoTypeName = ammoTypeNames[ammoIndex];
    return string.IsNullOrWhiteSpace(ammoTypeName) ? value : ammoTypeName;
  }

  private static IReadOnlyList<string> ReadAmmoTypeNames(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Items", "ItemTypes", "ItemAmmo.cs");
    if (!File.Exists(path))
      return [];

    var root = SyntaxParsingUtils.ParseCompilationUnit(path);
    var field = root.DescendantNodes()
      .OfType<FieldDeclarationSyntax>()
      .FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.Text == "ammoTypeNames"));

    if (field is null)
      return [];

    var variable = field.Declaration.Variables.FirstOrDefault(v => v.Identifier.Text == "ammoTypeNames");
    var initializer = variable?.Initializer?.Value;
    if (initializer is null)
      return [];

    var arrayValues = initializer
      .DescendantNodesAndSelf()
      .OfType<InitializerExpressionSyntax>()
      .SelectMany(i => i.Expressions)
      .OfType<LiteralExpressionSyntax>()
      .Where(l => l.Token.Value is string)
      .Select(l => l.Token.ValueText)
      .Where(value => !string.IsNullOrWhiteSpace(value))
      .ToArray();

    return arrayValues;
  }

  private static bool TryReadInt(object value, out int result)
  {
    switch (value)
    {
      case int intValue:
        result = intValue;
        return true;
      case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
        result = (int)longValue;
        return true;
      case float floatValue when floatValue >= int.MinValue && floatValue <= int.MaxValue && floatValue % 1 == 0:
        result = (int)floatValue;
        return true;
      case double doubleValue
        when doubleValue >= int.MinValue && doubleValue <= int.MaxValue && Math.Abs(doubleValue % 1) < double.Epsilon:
        result = (int)doubleValue;
        return true;
      case decimal decimalValue
        when decimalValue >= int.MinValue && decimalValue <= int.MaxValue && decimalValue % 1 == 0:
        result = (int)decimalValue;
        return true;
      default:
        result = 0;
        return false;
    }
  }

  private static object? TryParseLiteralLikeValue(ExpressionSyntax expression)
  {
    ExpressionSyntax cursor = expression;

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
          break;
      }

      break;
    }

    if (
      cursor is PrefixUnaryExpressionSyntax prefix
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

    return cursor switch
    {
      LiteralExpressionSyntax literal when literal.Token.Value is not null => literal.Token.Value,
      InvocationExpressionSyntax invocation when SyntaxParsingUtils.GetInvocationName(invocation) == "nameof" =>
        invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression switch
        {
          IdentifierNameSyntax identifier => identifier.Identifier.Text,
          MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
          _ => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString(),
        },
      TypeOfExpressionSyntax typeOfExpression => typeOfExpression.Type.ToString(),
      MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      _ => null,
    };
  }

  private sealed record SlotTypeResolver(
    string? DirectSlotType,
    string? SlotFieldName,
    IReadOnlyDictionary<string, string> EnumToSlotMap
  );
}
