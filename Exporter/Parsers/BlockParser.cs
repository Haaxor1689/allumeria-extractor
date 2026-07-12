using Microsoft.CodeAnalysis.CSharp.Syntax;

internal sealed class BlockParseResult
{
  public List<object> Blocks { get; } = [];
  public List<object> Items { get; } = [];
  public List<object> Loots { get; } = [];
}

internal static class BlockParser
{
  private const string BlockTypesRelativePath = "Blocks\\Blocks";

  private sealed class BlockTypeDefaults
  {
    public string? BaseTypeName { get; set; }
    public Dictionary<string, object?> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
  }

  public static BlockParseResult Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Blocks", "Blocks", "Block.cs");
    if (!File.Exists(path))
      return new BlockParseResult();

    var root = SyntaxParsingUtils.ParseCompilationUnit(path);
    var result = new BlockParseResult();
    var blockEntriesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
    var itemEntriesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
    var lootEntriesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
    var itemIdsBySymbol = ReadItemIdsBySymbol(sourceRoot);
    var stringArraysBySymbol = ReadStringArraysBySymbol(path);
    var constructorParamsByType = BuildConstructorParameterMap(sourceRoot);
    var blockTypeDefaultsByType = BuildBlockTypeDefaultsMap(sourceRoot);

    foreach (var field in SyntaxParsingUtils.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var initializer = variable.Initializer?.Value;
        if (initializer is null)
          continue;

        var ctor = SyntaxParsingUtils.TryGetRootObjectCreation(initializer);
        if (ctor is null || !ctor.Type.ToString().StartsWith("Block", StringComparison.Ordinal))
          continue;

        var invocations = SyntaxParsingUtils.FindInvocations(initializer).ToArray();
        var symbol = variable.Identifier.Text;
        var id = SyntaxParsingUtils.TryReadIdFromObjectCreation(ctor) ?? symbol;
        var constructorType = ctor.Type.ToString();
        var rawTypeName = constructorType.Split('.').Last().Trim();
        var typeName = NormalizeTypeName(constructorType);
        var resolvedTypeDefaults = ResolveBlockTypeDefaults(rawTypeName, typeName, blockTypeDefaultsByType);

        var textureInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetTexture"
        );
        var texturesInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetTextures"
        );
        var paintedTexturesInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetPaintedTextures"
        );
        var itemSpriteInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetItemSprite"
        );
        var blockModelInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetBlockModel"
        );
        var setLightEmissionInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetLightEmission"
        );
        var setSpawnEntryInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetSpawnEntry"
        );
        var setStandOnEffectInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetStandOnEffect"
        );
        var setCraftingTypeInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingUtils.GetInvocationName(inv) == "SetCraftingType"
        );

        var material = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "SetMaterial")
          .Select(inv => SyntaxParsingUtils.TryReadQualifiedMemberArg(inv, 0, "BlockMaterial"))
          .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(material))
          material = "dirt";

        var spawnDefinition = setSpawnEntryInvocation is null
          ? null
          : SyntaxParsingUtils.TryReadQualifiedMemberArg(setSpawnEntryInvocation, 0, "SpawnDefinition");

        var spawnRate = setSpawnEntryInvocation is null
          ? null
          : SyntaxParsingUtils.TryReadIntArg(setSpawnEntryInvocation, 1);

        var sellValue = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "SellValue")
          .Select(inv => SyntaxParsingUtils.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var category = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "SetCategory")
          .Select(inv => TryReadCategoryNames(inv, 0))
          .FirstOrDefault(values => values.Count > 0);

        var textures = TryReadStringArrayArg(texturesInvocation, 0, stringArraysBySymbol);
        textures = ApplyTextureOverrides(typeName, textures);
        if (textures.Count == 0)
        {
          var singleTexture = TryReadExpressionArg(textureInvocation, 0) as string;
          if (!string.IsNullOrWhiteSpace(singleTexture))
            textures = [singleTexture!];
        }

        var sprite = TryReadExpressionArg(itemSpriteInvocation, 0) as string;
        var blockModel = TryReadExpressionArg(blockModelInvocation, 0) as string;
        if (
          string.IsNullOrWhiteSpace(blockModel)
          && resolvedTypeDefaults.TryGetValue("blockModel", out var defaultBlockModel)
        )
        {
          blockModel = defaultBlockModel as string;
        }

        var standOnEffect = TryReadExpressionArg(setStandOnEffectInvocation, 0) as string;
        if (
          string.IsNullOrWhiteSpace(standOnEffect)
          && resolvedTypeDefaults.TryGetValue("standOnEffect", out var defaultStandOnEffect)
        )
        {
          standOnEffect = defaultStandOnEffect as string;
        }

        var craftingStation = setCraftingTypeInvocation is null
          ? null
          : SyntaxParsingUtils.TryReadQualifiedMemberArg(setCraftingTypeInvocation, 0, "CraftingStation");

        if (
          string.IsNullOrWhiteSpace(craftingStation)
          && typeName == "CraftingStation"
          && resolvedTypeDefaults.TryGetValue("craftingStation", out var defaultCraftingStation)
        )
        {
          craftingStation = defaultCraftingStation as string;
        }

        var decorationScore = invocations
          .Where(inv => SyntaxParsingUtils.GetInvocationName(inv) == "SetDecorationScore")
          .Select(inv => TryReadExpressionArg(inv, 0))
          .FirstOrDefault(value => value is float or double or decimal);

        var lightEmission = TryReadIntTriple(setLightEmissionInvocation, 0, 1, 2);
        var invocationNames = invocations
          .Select(SyntaxParsingUtils.GetInvocationName)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .ToHashSet(StringComparer.Ordinal);

        var hidden = invocationNames.Contains("Hide");
        var interactible =
          GetDefaultBool(resolvedTypeDefaults, "interactible") == true || invocationNames.Contains("MakeInteractible");
        var canBeFelled = GetDefaultBool(resolvedTypeDefaults, "canBeFelled") == true;
        var isCrop = GetDefaultBool(resolvedTypeDefaults, "isCrop") == true;
        var needsSupport = GetDefaultBool(resolvedTypeDefaults, "needsSupport") == true;
        var canBeShaped = invocationNames.Contains("AutoGenVariants");
        var spreadsSelf = invocationNames.Contains("MakeSpreadSelf");

        if (invocationNames.Contains("MakeNeedSupport"))
          needsSupport = true;

        var entry = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = id };
        var itemEntry = CreateDefaultItemEntry(id, sprite, sellValue);

        if (typeName != "Block")
          entry["class"] = typeName;

        entry["material"] = material;

        if (!string.IsNullOrWhiteSpace(spawnDefinition))
          entry["spawn"] = spawnDefinition;

        if (spawnRate.HasValue && spawnRate.Value > 0)
          entry["spawnRate"] = spawnRate.Value;

        if (hidden)
          entry["hidden"] = true;

        if (interactible)
          entry["interactible"] = true;

        if (needsSupport)
          entry["needsSupport"] = true;

        if (canBeFelled)
          entry["canBeFelled"] = true;

        if (isCrop)
          entry["isCrop"] = true;

        if (canBeShaped)
          entry["canBeShaped"] = true;

        if (spreadsSelf)
          entry["spreadsSelf"] = true;

        if (category is { Count: > 0 })
          itemEntry["category"] = category;

        if (textures.Count > 0)
          entry["textures"] = textures;

        if (!string.IsNullOrWhiteSpace(craftingStation))
          entry["craftingStation"] = craftingStation;

        if (!string.IsNullOrWhiteSpace(blockModel))
          entry["blockModel"] = blockModel;

        if (!string.IsNullOrWhiteSpace(standOnEffect))
          entry["standOnEffect"] = standOnEffect;

        if (decorationScore is float f && f != 0f)
          entry["decorationScore"] = f;
        else if (decorationScore is double d && d != 0d)
          entry["decorationScore"] = d;
        else if (decorationScore is decimal m && m != 0m)
          entry["decorationScore"] = m;

        if (lightEmission is { Count: 3 })
          entry["lightEmission"] = lightEmission;

        AddWhitelistedConstructorFields(entry, ctor, constructorType, typeName, constructorParamsByType);

        result.Blocks.Add(entry);
        result.Items.Add(itemEntry);
        if (!string.IsNullOrWhiteSpace(id))
        {
          blockEntriesById[id] = entry;
          itemEntriesById[id] = itemEntry;
        }
      }
    }

    ApplyProjectWideBlockAssignments(
      sourceRoot,
      blockEntriesById,
      itemEntriesById,
      lootEntriesById,
      result.Items,
      result.Loots,
      itemIdsBySymbol
    );

    return result;
  }

  private static void ApplyProjectWideBlockAssignments(
    string sourceRoot,
    IReadOnlyDictionary<string, Dictionary<string, object?>> blockEntriesById,
    IDictionary<string, Dictionary<string, object?>> itemEntriesById,
    IDictionary<string, Dictionary<string, object?>> lootEntriesById,
    IList<object> items,
    IList<object> loots,
    IReadOnlyDictionary<string, string> itemIdsBySymbol
  )
  {
    foreach (var file in SyntaxParsingUtils.EnumerateSourceFiles(sourceRoot))
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);

      foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
      {
        var invocationName = SyntaxParsingUtils.GetInvocationName(invocation);

        switch (invocationName)
        {
          case "SetLoot":
            ApplySetLootInvocation(invocation, blockEntriesById);
            break;
          case "SetDropItem":
            ApplySetDropItemInvocation(invocation, blockEntriesById, lootEntriesById, loots);
            break;
          case "OverwriteItem":
            ApplyOverwriteItemInvocation(invocation, blockEntriesById, itemEntriesById, items, itemIdsBySymbol);
            break;
          case "SetRarity":
            ApplySetRarityInvocation(invocation, blockEntriesById, itemEntriesById);
            break;
          case "TargetLiquid":
            ApplyTargetLiquidInvocation(invocation, blockEntriesById, itemEntriesById);
            break;
          case "AddTag":
            ApplyAddTagInvocation(invocation, blockEntriesById, itemEntriesById);
            break;
        }
      }

      foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
      {
        ApplyBlockAssignment(assignment, blockEntriesById);
      }
    }
  }

  private static void ApplySetLootInvocation(
    InvocationExpressionSyntax invocation,
    IReadOnlyDictionary<string, Dictionary<string, object?>> blockEntriesById
  )
  {
    if (!TryReadBlockTargetId(invocation, out var blockId))
      return;

    if (!TryGetBlockEntry(blockEntriesById, blockId, out var blockEntry))
      return;

    var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    var lootId = TryReadLootDescriptionReference(argument);
    if (string.IsNullOrWhiteSpace(lootId))
      return;

    blockEntry["loot"] = lootId;
  }

  private static void ApplySetDropItemInvocation(
    InvocationExpressionSyntax invocation,
    IReadOnlyDictionary<string, Dictionary<string, object?>> blockEntriesById,
    IDictionary<string, Dictionary<string, object?>> lootEntriesById,
    IList<object> loots
  )
  {
    if (!TryReadBlockTargetId(invocation, out var blockId))
      return;

    if (!TryGetBlockEntry(blockEntriesById, blockId, out var blockEntry))
      return;

    var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    var dropItem = NormalizeEntityExpression(argument?.ToString());
    if (string.IsNullOrWhiteSpace(dropItem))
      return;

    EnsureSyntheticDropLoot(dropItem!, lootEntriesById, loots);
    blockEntry["loot"] = dropItem;
  }

  private static void ApplyOverwriteItemInvocation(
    InvocationExpressionSyntax invocation,
    IReadOnlyDictionary<string, Dictionary<string, object?>> blockEntriesById,
    IDictionary<string, Dictionary<string, object?>> itemEntriesById,
    IList<object> items,
    IReadOnlyDictionary<string, string> itemIdsBySymbol
  )
  {
    if (!TryReadBlockTargetId(invocation, out var blockId))
      return;

    if (!TryGetBlockEntry(blockEntriesById, blockId, out var blockEntry))
      return;

    if (!TryGetItemEntry(itemEntriesById, blockId, out var blockItemEntry))
      return;

    var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    var itemId = NormalizeEntityExpression(argument?.ToString(), itemIdsBySymbol);
    if (string.IsNullOrWhiteSpace(itemId))
      return;

    blockEntry["item"] = itemId;

    var itemEntry = RenameOrMergeItemEntry(items, itemEntriesById, blockItemEntry, blockId!, itemId!);
    itemEntry["block"] = blockId!;

    var placeable =
      invocation.ArgumentList.Arguments.Count > 1
        ? TryReadBoolLiteral(invocation.ArgumentList.Arguments[1].Expression)
        : null;

    if (placeable == false)
      RemoveTag(itemEntry, "can_place");
    else
      AddOrUpdateTag(itemEntry, "can_place", true);
  }

  private static void ApplySetRarityInvocation(
    InvocationExpressionSyntax invocation,
    IReadOnlyDictionary<string, Dictionary<string, object?>> blockEntriesById,
    IDictionary<string, Dictionary<string, object?>> itemEntriesById
  )
  {
    if (!TryReadBlockItemTargetId(invocation, out var blockId))
      return;

    if (!TryGetBlockEntry(blockEntriesById, blockId, out var blockEntry))
      return;

    if (!TryGetLinkedItemEntry(blockEntry, blockId, itemEntriesById, out var itemEntry))
      return;

    var rarity = SyntaxParsingUtils.TryReadIntArg(invocation, 0);
    if (rarity.HasValue)
      itemEntry["rarity"] = rarity.Value;
  }

  private static void ApplyTargetLiquidInvocation(
    InvocationExpressionSyntax invocation,
    IReadOnlyDictionary<string, Dictionary<string, object?>> blockEntriesById,
    IDictionary<string, Dictionary<string, object?>> itemEntriesById
  )
  {
    if (!TryReadBlockItemTargetId(invocation, out var blockId))
      return;

    if (!TryGetBlockEntry(blockEntriesById, blockId, out var blockEntry))
      return;

    if (!TryGetLinkedItemEntry(blockEntry, blockId, itemEntriesById, out var itemEntry))
      return;

    itemEntry["targetLiquid"] = true;
  }

  private static void ApplyAddTagInvocation(
    InvocationExpressionSyntax invocation,
    IReadOnlyDictionary<string, Dictionary<string, object?>> blockEntriesById,
    IDictionary<string, Dictionary<string, object?>> itemEntriesById
  )
  {
    if (!TryReadBlockItemTargetId(invocation, out var blockId))
      return;

    if (!TryGetBlockEntry(blockEntriesById, blockId, out var blockEntry))
      return;

    if (!TryGetLinkedItemEntry(blockEntry, blockId, itemEntriesById, out var itemEntry))
      return;

    var tagKey = SyntaxParsingUtils.TryReadQualifiedMemberArg(invocation, 0, "ItemTag");
    if (string.IsNullOrWhiteSpace(tagKey))
      return;

    var tagValue =
      invocation.ArgumentList.Arguments.Count > 1
        ? TryParseLiteralLikeValue(invocation.ArgumentList.Arguments[1].Expression) ?? true
        : true;

    AddOrUpdateTag(itemEntry, tagKey!, tagValue);
  }

  private static void ApplyBlockAssignment(
    AssignmentExpressionSyntax assignment,
    IReadOnlyDictionary<string, Dictionary<string, object?>> blockEntriesById
  )
  {
    var left = Unwrap(assignment.Left);
    if (left is not MemberAccessExpressionSyntax memberAccess)
      return;

    if (!TryReadBlockIdFromExpression(memberAccess.Expression, out var blockId))
      return;

    if (!TryGetBlockEntry(blockEntriesById, blockId, out var blockEntry))
      return;

    var fieldName = memberAccess.Name.Identifier.Text;
    switch (fieldName)
    {
      case "canBeFelled":
        if (TryReadBoolLiteral(assignment.Right) is bool canBeFelled)
        {
          if (canBeFelled)
            blockEntry["canBeFelled"] = true;
          else
            blockEntry.Remove("canBeFelled");
        }
        break;
      case "harvestLoot":
        var harvestLoot = TryReadLootDescriptionReference(assignment.Right);
        if (!string.IsNullOrWhiteSpace(harvestLoot))
          blockEntry["harvestLoot"] = harvestLoot;
        break;
    }
  }

  private static bool TryGetBlockEntry(
    IReadOnlyDictionary<string, Dictionary<string, object?>> blockEntriesById,
    string? blockId,
    out Dictionary<string, object?> entry
  )
  {
    if (!string.IsNullOrWhiteSpace(blockId) && blockEntriesById.TryGetValue(blockId, out var found))
    {
      entry = found;
      return true;
    }

    entry = null!;
    return false;
  }

  private static bool TryGetItemEntry(
    IDictionary<string, Dictionary<string, object?>> itemEntriesById,
    string? itemId,
    out Dictionary<string, object?> entry
  )
  {
    if (!string.IsNullOrWhiteSpace(itemId) && itemEntriesById.TryGetValue(itemId, out var found))
    {
      entry = found;
      return true;
    }

    entry = null!;
    return false;
  }

  private static bool TryGetLinkedItemEntry(
    IReadOnlyDictionary<string, object?> blockEntry,
    string? blockId,
    IDictionary<string, Dictionary<string, object?>> itemEntriesById,
    out Dictionary<string, object?> itemEntry
  )
  {
    var itemId = GetLinkedItemId(blockEntry, blockId);
    return TryGetItemEntry(itemEntriesById, itemId, out itemEntry);
  }

  private static string? GetLinkedItemId(IReadOnlyDictionary<string, object?> blockEntry, string? blockId)
  {
    if (blockEntry.TryGetValue("item", out var linkedItemValue))
    {
      var linkedItemId = linkedItemValue?.ToString();
      if (!string.IsNullOrWhiteSpace(linkedItemId))
        return linkedItemId;
    }

    return blockId;
  }

  private static bool TryReadBlockTargetId(InvocationExpressionSyntax invocation, out string? blockId)
  {
    blockId = null;

    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
      return false;

    return TryReadBlockIdFromExpression(memberAccess.Expression, out blockId);
  }

  private static bool TryReadBlockItemTargetId(InvocationExpressionSyntax invocation, out string? blockId)
  {
    blockId = null;

    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
      return false;

    return TryReadBlockItemIdFromExpression(memberAccess.Expression, out blockId);
  }

  private static bool TryReadBlockIdFromExpression(ExpressionSyntax expression, out string? blockId)
  {
    blockId = null;

    var reduced = Unwrap(expression);
    if (
      reduced is MemberAccessExpressionSyntax memberAccess
      && memberAccess.Expression is IdentifierNameSyntax owner
      && owner.Identifier.Text == "Block"
    )
    {
      blockId = memberAccess.Name.Identifier.Text;
      return true;
    }

    return false;
  }

  private static bool TryReadBlockItemIdFromExpression(ExpressionSyntax expression, out string? blockId)
  {
    blockId = null;

    var reduced = Unwrap(expression);
    if (reduced is not MemberAccessExpressionSyntax memberAccess || memberAccess.Name.Identifier.Text != "item")
      return false;

    return TryReadBlockIdFromExpression(memberAccess.Expression, out blockId);
  }

  private static bool? TryReadBoolLiteral(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);
    return reduced switch
    {
      LiteralExpressionSyntax literal when literal.Token.Value is bool value => value,
      _ => null,
    };
  }

  private static string? TryReadLootDescriptionReference(ExpressionSyntax? expression)
  {
    if (expression is null)
      return null;

    var reduced = Unwrap(expression);

    if (
      reduced is MemberAccessExpressionSyntax memberAccess
      && memberAccess.Expression is IdentifierNameSyntax owner
      && owner.Identifier.Text == "LootDescription"
    )
    {
      return memberAccess.Name.Identifier.Text;
    }

    var creation = SyntaxParsingUtils.TryGetRootObjectCreation(reduced);
    if (creation is not null && NormalizeTypeName(creation.Type.ToString()) == "LootDescription")
      return SyntaxParsingUtils.TryReadIdFromObjectCreation(creation);

    return null;
  }

  private static string? NormalizeEntityExpression(
    string? expression,
    IReadOnlyDictionary<string, string>? itemIdsBySymbol = null
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
      if (itemIdsBySymbol is not null && itemIdsBySymbol.TryGetValue(itemSymbol, out var itemId))
        return itemId;
      return itemSymbol;
    }

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

  private static void AddWhitelistedConstructorFields(
    IDictionary<string, object?> entry,
    ObjectCreationExpressionSyntax ctor,
    string constructorType,
    string typeName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> constructorParamsByType
  )
  {
    if (typeName == "Block")
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
    }

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

      var fieldName = NormalizeConstructorFieldName(parameterName);
      switch (fieldName)
      {
        case "isMutated" when value is bool isMutated && isMutated:
          entry["isMutated"] = true;
          break;
        case "keyItem":
          entry["keyItem"] = value;
          break;
        case "treeType":
          entry["treeType"] = value;
          break;
      }
    }
  }

  private static Dictionary<string, object?> CreateDefaultItemEntry(string id, string? sprite, int? sellValue)
  {
    var itemEntry = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
      ["id"] = id,
      ["block"] = id,
      ["tags"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["can_place"] = true },
    };

    if (!string.IsNullOrWhiteSpace(sprite) && sprite != id)
      itemEntry["sprite"] = sprite;

    if (sellValue.HasValue)
      itemEntry["sellValue"] = sellValue.Value;

    return itemEntry;
  }

  private static void EnsureSyntheticDropLoot(
    string itemId,
    IDictionary<string, Dictionary<string, object?>> lootEntriesById,
    IList<object> loots
  )
  {
    if (lootEntriesById.ContainsKey(itemId))
      return;

    var lootEntry = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
      ["id"] = itemId,
      ["group"] = "Misc",
      ["entries"] = new object[]
      {
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["item"] = itemId, ["amount"] = 1 },
      },
    };

    loots.Add(lootEntry);
    lootEntriesById[itemId] = lootEntry;
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

  private static Dictionary<string, object?> RenameOrMergeItemEntry(
    IList<object> items,
    IDictionary<string, Dictionary<string, object?>> itemEntriesById,
    Dictionary<string, object?> sourceEntry,
    string sourceItemId,
    string targetItemId
  )
  {
    if (string.Equals(sourceItemId, targetItemId, StringComparison.OrdinalIgnoreCase))
      return sourceEntry;

    itemEntriesById.Remove(sourceItemId);

    if (itemEntriesById.TryGetValue(targetItemId, out var existingTarget))
    {
      MergeEntries(existingTarget, sourceEntry);
      items.Remove(sourceEntry);
      return existingTarget;
    }

    sourceEntry["id"] = targetItemId;
    itemEntriesById[targetItemId] = sourceEntry;
    return sourceEntry;
  }

  private static void MergeEntries(
    IDictionary<string, object?> destination,
    IReadOnlyDictionary<string, object?> source
  )
  {
    foreach (var (key, value) in source)
    {
      if (key == "id")
        continue;

      if (key == "tags" && value is IReadOnlyDictionary<string, object?> sourceTags)
      {
        foreach (var (tagKey, tagValue) in sourceTags)
          AddOrUpdateTag(destination, tagKey, tagValue);
        continue;
      }

      if (!destination.ContainsKey(key))
        destination[key] = value;
    }
  }

  private static void AddOrUpdateTag(IDictionary<string, object?> itemEntry, string tagKey, object? tagValue)
  {
    if (!itemEntry.TryGetValue("tags", out var tagsValue) || tagsValue is not Dictionary<string, object?> tags)
    {
      tags = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
      itemEntry["tags"] = tags;
    }

    tags[tagKey] = tagValue;
  }

  private static void RemoveTag(IDictionary<string, object?> itemEntry, string tagKey)
  {
    if (!itemEntry.TryGetValue("tags", out var tagsValue) || tagsValue is not Dictionary<string, object?> tags)
      return;

    tags.Remove(tagKey);
    if (tags.Count == 0)
      itemEntry.Remove("tags");
  }

  private static string NormalizeConstructorFieldName(string parameterName)
  {
    if (parameterName.EndsWith("ID", StringComparison.Ordinal) && parameterName.Length > 2)
      return parameterName[..^2];
    return parameterName;
  }

  private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildConstructorParameterMap(string sourceRoot)
  {
    var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["Block"] = ["strID"] };

    var blockTypesRoot = Path.Combine(sourceRoot, BlockTypesRelativePath);
    if (!Directory.Exists(blockTypesRoot))
      return map;

    foreach (var file in Directory.EnumerateFiles(blockTypesRoot, "*.cs", SearchOption.TopDirectoryOnly))
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

  private static IReadOnlyDictionary<string, BlockTypeDefaults> BuildBlockTypeDefaultsMap(string sourceRoot)
  {
    var map = new Dictionary<string, BlockTypeDefaults>(StringComparer.OrdinalIgnoreCase);
    var blockTypesRoot = Path.Combine(sourceRoot, BlockTypesRelativePath);

    if (!Directory.Exists(blockTypesRoot))
      return map;

    foreach (var file in Directory.EnumerateFiles(blockTypesRoot, "*.cs", SearchOption.TopDirectoryOnly))
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);

      foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
      {
        var typeName = declaration.Identifier.Text;
        if (string.IsNullOrWhiteSpace(typeName))
          continue;

        if (!map.TryGetValue(typeName, out var typeDefaults))
        {
          typeDefaults = new BlockTypeDefaults();
          map[typeName] = typeDefaults;
        }

        if (declaration.BaseList?.Types.FirstOrDefault() is { } baseType)
          typeDefaults.BaseTypeName = NormalizeTypeReferenceName(baseType.Type.ToString());

        foreach (var constructor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
          if (constructor.Body is not null)
          {
            foreach (var statement in constructor.Body.Statements.OfType<ExpressionStatementSyntax>())
            {
              ApplyDefaultAssignment(typeDefaults.Attributes, statement.Expression);
              ApplyDefaultInvocation(typeDefaults.Attributes, statement.Expression);
            }
          }

          if (constructor.ExpressionBody is not null)
          {
            ApplyDefaultAssignment(typeDefaults.Attributes, constructor.ExpressionBody.Expression);
            ApplyDefaultInvocation(typeDefaults.Attributes, constructor.ExpressionBody.Expression);
          }
        }
      }
    }

    return map;
  }

  private static IReadOnlyDictionary<string, object?> ResolveBlockTypeDefaults(
    string rawTypeName,
    string normalizedTypeName,
    IReadOnlyDictionary<string, BlockTypeDefaults> blockTypeDefaultsByType
  )
  {
    var lookupTypeName = !string.IsNullOrWhiteSpace(rawTypeName) ? rawTypeName : normalizedTypeName;

    if (string.IsNullOrWhiteSpace(lookupTypeName))
      return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    if (!blockTypeDefaultsByType.ContainsKey(lookupTypeName) && blockTypeDefaultsByType.ContainsKey(normalizedTypeName))
      lookupTypeName = normalizedTypeName;

    var resolved = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    MergeBlockTypeDefaults(lookupTypeName, blockTypeDefaultsByType, resolved, visited);
    return resolved;
  }

  private static void MergeBlockTypeDefaults(
    string typeName,
    IReadOnlyDictionary<string, BlockTypeDefaults> blockTypeDefaultsByType,
    IDictionary<string, object?> destination,
    ISet<string> visited
  )
  {
    if (!visited.Add(typeName))
      return;

    if (!blockTypeDefaultsByType.TryGetValue(typeName, out var typeDefaults))
      return;

    if (!string.IsNullOrWhiteSpace(typeDefaults.BaseTypeName))
      MergeBlockTypeDefaults(typeDefaults.BaseTypeName!, blockTypeDefaultsByType, destination, visited);

    foreach (var (key, value) in typeDefaults.Attributes)
      destination[key] = value;
  }

  private static bool? GetDefaultBool(IReadOnlyDictionary<string, object?> values, string key)
  {
    return values.TryGetValue(key, out var raw) && raw is bool boolean ? boolean : null;
  }

  private static void ApplyDefaultAssignment(IDictionary<string, object?> attributes, ExpressionSyntax expression)
  {
    if (expression is not AssignmentExpressionSyntax assignment)
      return;

    var fieldName = TryReadAssignedFieldName(assignment.Left);
    if (string.IsNullOrWhiteSpace(fieldName))
      return;

    var value = TryParseLiteralLikeValue(assignment.Right);
    if (value is null)
      return;

    var key = fieldName switch
    {
      "solid" => "solid",
      "semisolid" => "semiSolid",
      "transparent" => "transparent",
      "blocksLight" => "blocksLight",
      "needsSupport" => "needsSupport",
      _ => fieldName,
    };

    attributes[key] = value;
  }

  private static void ApplyDefaultInvocation(IDictionary<string, object?> attributes, ExpressionSyntax expression)
  {
    if (expression is not InvocationExpressionSyntax invocation)
      return;

    var invocationName = SyntaxParsingUtils.GetInvocationName(invocation);
    switch (invocationName)
    {
      case "MakeSolid":
        attributes["solid"] = true;
        attributes["blocksLight"] = true;
        break;
      case "MakeSemiSolid":
        attributes["solid"] = true;
        attributes["semiSolid"] = true;
        attributes["blocksLight"] = false;
        break;
      case "MakeTransparent":
        attributes["transparent"] = true;
        break;
      case "MakeNeedSupport":
        attributes["needsSupport"] = true;
        break;
      case "MakeInteractible":
        attributes["interactible"] = true;
        break;
      case "SetBlockModel":
        if (TryReadExpressionArg(invocation, 0) is string blockModel && !string.IsNullOrWhiteSpace(blockModel))
          attributes["blockModel"] = blockModel;
        break;
      case "SetStandOnEffect":
        if (TryReadExpressionArg(invocation, 0) is string standOnEffect && !string.IsNullOrWhiteSpace(standOnEffect))
          attributes["standOnEffect"] = standOnEffect;
        break;
    }
  }

  private static string? TryReadAssignedFieldName(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);

    return reduced switch
    {
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      MemberAccessExpressionSyntax memberAccess when memberAccess.Expression is ThisExpressionSyntax => memberAccess
        .Name
        .Identifier
        .Text,
      MemberAccessExpressionSyntax memberAccess when memberAccess.Expression is IdentifierNameSyntax => memberAccess
        .Name
        .Identifier
        .Text,
      _ => null,
    };
  }

  private static string NormalizeTypeReferenceName(string typeName)
  {
    return typeName.Split('.').Last().Trim();
  }

  private static string NormalizeTypeName(string constructorType)
  {
    var simpleName = constructorType.Split('.').Last().Trim();
    if (simpleName == "Block")
      return "Block";
    return simpleName.StartsWith("Block", StringComparison.Ordinal) ? simpleName[5..] : simpleName;
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

  private static IReadOnlyList<string> TryReadStringArrayArg(
    InvocationExpressionSyntax? invocation,
    int argumentIndex,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? stringArraysBySymbol = null
  )
  {
    if (invocation is null || invocation.ArgumentList.Arguments.Count <= argumentIndex)
      return [];

    var expression = invocation.ArgumentList.Arguments[argumentIndex].Expression;

    if (
      expression is ArrayCreationExpressionSyntax arrayCreation
      || expression is ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer }
    )
    {
      var arrayInitializer = expression is ArrayCreationExpressionSyntax arrayCreationExpression
        ? arrayCreationExpression.Initializer
        : ((ImplicitArrayCreationExpressionSyntax)expression).Initializer;

      if (arrayInitializer is null)
        return [];

      return arrayInitializer
        .Expressions.Select(TryParseLiteralLikeValue)
        .OfType<string>()
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();
    }

    if (stringArraysBySymbol is not null && TryParseLiteralLikeValue(expression) is string symbolName)
      return stringArraysBySymbol.TryGetValue(symbolName, out var values) ? values : [];

    return [];
  }

  private static IReadOnlyList<string> ApplyTextureOverrides(string typeName, IReadOnlyList<string> textures)
  {
    if (textures.Count == 0)
      return textures;

    return typeName switch
    {
      "ToggleLamp" => BuildToggleLampTextures(textures),
      "Pumpkin" => BuildPumpkinTextures(textures),
      _ => textures,
    };
  }

  private static IReadOnlyList<string> BuildToggleLampTextures(IReadOnlyList<string> textures)
  {
    return textures.Take(textures.Count / 2).Select(t => t + "off").ToArray();
  }

  private static IReadOnlyList<string> BuildPumpkinTextures(IReadOnlyList<string> textures)
  {
    return textures.Where((_, index) => index % 2 == 1).Select(t => t + "off").ToArray();
  }

  private static Dictionary<string, IReadOnlyList<string>> ReadStringArraysBySymbol(string path)
  {
    var root = SyntaxParsingUtils.ParseCompilationUnit(path);
    var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    foreach (var field in SyntaxParsingUtils.FindPublicStaticFields(root))
    {
      if (field.Declaration.Type.ToString() != "string[]")
        continue;

      foreach (var variable in field.Declaration.Variables)
      {
        if (variable.Initializer?.Value is not ArrayCreationExpressionSyntax arrayCreation)
          continue;

        if (arrayCreation.Initializer is null)
          continue;

        var values = arrayCreation
          .Initializer.Expressions.Select(TryParseLiteralLikeValue)
          .OfType<string>()
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .ToArray();

        if (values.Length > 0)
          map[variable.Identifier.Text] = values;
      }
    }

    return map;
  }

  private static IReadOnlyList<int> TryReadIntTriple(
    InvocationExpressionSyntax? invocation,
    int firstIndex,
    int secondIndex,
    int thirdIndex
  )
  {
    if (invocation is null)
      return [];

    var first = TryConvertToInt(TryReadExpressionArg(invocation, firstIndex));
    var second = TryConvertToInt(TryReadExpressionArg(invocation, secondIndex));
    var third = TryConvertToInt(TryReadExpressionArg(invocation, thirdIndex));

    return first.HasValue && second.HasValue && third.HasValue ? [first.Value, second.Value, third.Value] : [];
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

  private static object? TryReadExpressionArg(InvocationExpressionSyntax? invocation, int argumentIndex)
  {
    if (invocation is null || invocation.ArgumentList.Arguments.Count <= argumentIndex)
      return null;

    return TryParseLiteralLikeValue(invocation.ArgumentList.Arguments[argumentIndex].Expression);
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
}
