using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class BlockParser
{
  private const string BlockTypesRelativePath = "Blocks\\Blocks";

  public static List<object> Parse(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "Blocks", "Blocks", "Block.cs");
    if (!File.Exists(path))
      return [];

    var root = SyntaxParsingHelpers.ParseCompilationUnit(path);
    var list = new List<object>();
    var constructorParamsByType = BuildConstructorParameterMap(sourceRoot);
    var defaultCraftingTypesByType = BuildDefaultCraftingTypeMap(sourceRoot);

    foreach (var field in SyntaxParsingHelpers.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var initializer = variable.Initializer?.Value;
        if (initializer is null)
          continue;

        var ctor = SyntaxParsingHelpers.TryGetRootObjectCreation(initializer);
        if (ctor is null || !ctor.Type.ToString().StartsWith("Block", StringComparison.Ordinal))
          continue;

        var invocations = SyntaxParsingHelpers.FindInvocations(initializer).ToArray();
        var symbol = variable.Identifier.Text;
        var id = SyntaxParsingHelpers.TryReadIdFromObjectCreation(ctor) ?? symbol;
        var constructorType = ctor.Type.ToString();
        var typeName = NormalizeTypeName(constructorType);

        var textureInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetTexture"
        );
        var texturesInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetTextures"
        );
        var paintedTexturesInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetPaintedTextures"
        );
        var itemSpriteInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetItemSprite"
        );
        var blockModelInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetBlockModel"
        );
        var setLightEmissionInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetLightEmission"
        );
        var setSpawnEntryInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetSpawnEntry"
        );
        var setStandOnEffectInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetStandOnEffect"
        );
        var setCraftingTypeInvocation = invocations.LastOrDefault(inv =>
          SyntaxParsingHelpers.GetInvocationName(inv) == "SetCraftingType"
        );

        var material = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "SetMaterial")
          .Select(inv => SyntaxParsingHelpers.TryReadQualifiedMemberArg(inv, 0, "BlockMaterial"))
          .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        var spawnDefinition = setSpawnEntryInvocation is null
          ? null
          : SyntaxParsingHelpers.TryReadQualifiedMemberArg(setSpawnEntryInvocation, 0, "SpawnDefinition");

        var spawnRate = setSpawnEntryInvocation is null
          ? null
          : SyntaxParsingHelpers.TryReadIntArg(setSpawnEntryInvocation, 1);

        var sellValue = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "SellValue")
          .Select(inv => SyntaxParsingHelpers.TryReadIntArg(inv, 0))
          .FirstOrDefault(value => value.HasValue);

        var category = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "SetCategory")
          .Select(inv => TryReadCategoryNames(inv, 0))
          .FirstOrDefault(values => values.Count > 0);

        var textures = TryReadStringArrayArg(texturesInvocation, 0);
        if (textures.Count == 0)
        {
          var singleTexture = TryReadExpressionArg(textureInvocation, 0) as string;
          if (!string.IsNullOrWhiteSpace(singleTexture))
            textures = [singleTexture!];
        }

        var sprite = TryReadExpressionArg(itemSpriteInvocation, 0) as string;
        var blockModel = TryReadExpressionArg(blockModelInvocation, 0) as string;
        var standOnEffect = TryReadExpressionArg(setStandOnEffectInvocation, 0) as string;
        var craftingType = setCraftingTypeInvocation is null
          ? null
          : SyntaxParsingHelpers.TryReadQualifiedMemberArg(setCraftingTypeInvocation, 0, "CraftingStation");

        if (string.IsNullOrWhiteSpace(craftingType))
        {
          var rawTypeName = constructorType.Split('.').Last().Trim();
          if (!defaultCraftingTypesByType.TryGetValue(rawTypeName, out craftingType))
            defaultCraftingTypesByType.TryGetValue(typeName, out craftingType);
        }

        var decorationScore = invocations
          .Where(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "SetDecorationScore")
          .Select(inv => TryReadExpressionArg(inv, 0))
          .FirstOrDefault(value => value is float or double or decimal);

        var lightEmission = TryReadIntTriple(setLightEmissionInvocation, 0, 1, 2);

        var hidden = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "Hide");
        var solid = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "MakeSolid");
        var semiSolid = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "MakeSemiSolid");
        var transparent = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "MakeTransparent");
        var interactible = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "MakeInteractible");
        var needSupport = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "MakeNeedSupport");
        var canBeShaped = invocations.Any(inv => SyntaxParsingHelpers.GetInvocationName(inv) == "AutoGenVariants");

        var entry = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = id };

        if (typeName != "Block")
          entry["type"] = typeName;

        if (!string.IsNullOrWhiteSpace(material))
          entry["material"] = material;

        if (!string.IsNullOrWhiteSpace(spawnDefinition))
          entry["spawn"] = spawnDefinition;

        if (spawnRate.HasValue && spawnRate.Value > 0)
          entry["spawnRate"] = spawnRate.Value;

        if (sellValue.HasValue)
          entry["sellValue"] = sellValue.Value;

        if (hidden)
          entry["hidden"] = true;

        if (solid)
          entry["solid"] = true;

        if (semiSolid)
          entry["semiSolid"] = true;

        if (transparent)
          entry["transparent"] = true;

        if (interactible)
          entry["interactible"] = true;

        if (needSupport)
          entry["needSupport"] = true;

        if (canBeShaped)
          entry["canBeShaped"] = true;

        if (category is { Count: > 0 })
          entry["category"] = category;

        if (textures.Count > 0)
          entry["textures"] = textures;

        if (!string.IsNullOrWhiteSpace(sprite) && sprite != id)
          entry["sprite"] = sprite;

        if (!string.IsNullOrWhiteSpace(blockModel))
          entry["blockModel"] = blockModel;

        if (!string.IsNullOrWhiteSpace(standOnEffect))
          entry["standOnEffect"] = standOnEffect;

        if (!string.IsNullOrWhiteSpace(craftingType))
          entry["craftingType"] = craftingType;

        if (decorationScore is not null)
          entry["decorationScore"] = decorationScore;

        if (lightEmission is { Count: 3 })
          entry["lightEmission"] = lightEmission;

        AddExtraConstructorFields(entry, ctor, constructorType, typeName, constructorParamsByType);

        list.Add(entry);
      }
    }

    return list;
  }

  private static void AddExtraConstructorFields(
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
    var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["Block"] = ["strID"] };

    var blockTypesRoot = Path.Combine(sourceRoot, BlockTypesRelativePath);
    if (!Directory.Exists(blockTypesRoot))
      return map;

    foreach (var file in Directory.EnumerateFiles(blockTypesRoot, "*.cs", SearchOption.TopDirectoryOnly))
    {
      var root = SyntaxParsingHelpers.ParseCompilationUnit(file);
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

  private static IReadOnlyDictionary<string, string> BuildDefaultCraftingTypeMap(string sourceRoot)
  {
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var blockTypesRoot = Path.Combine(sourceRoot, BlockTypesRelativePath);

    if (!Directory.Exists(blockTypesRoot))
      return map;

    foreach (var file in Directory.EnumerateFiles(blockTypesRoot, "*.cs", SearchOption.TopDirectoryOnly))
    {
      var root = SyntaxParsingHelpers.ParseCompilationUnit(file);

      foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
      {
        var typeName = declaration.Identifier.Text;
        if (string.IsNullOrWhiteSpace(typeName))
          continue;

        var defaultValue = TryReadDefaultCraftingType(declaration);
        if (!string.IsNullOrWhiteSpace(defaultValue))
          map[typeName] = defaultValue;
      }
    }

    return map;
  }

  private static string? TryReadDefaultCraftingType(ClassDeclarationSyntax declaration)
  {
    // Handle field initializer forms like: private CraftingStation craftingStation = CraftingStation.work_bench;
    foreach (var field in declaration.Members.OfType<FieldDeclarationSyntax>())
    {
      foreach (var variable in field.Declaration.Variables)
      {
        if (!string.Equals(variable.Identifier.Text, "craftingStation", StringComparison.Ordinal))
          continue;

        var fromInitializer = TryReadCraftingStationValue(variable.Initializer?.Value);
        if (!string.IsNullOrWhiteSpace(fromInitializer))
          return fromInitializer;
      }
    }

    // Handle constructor assignment forms like: this.craftingStation = CraftingStation.work_bench;
    foreach (var constructor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
    {
      if (constructor.Body is null)
        continue;

      foreach (var assignment in constructor.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
      {
        if (!IsCraftingStationTarget(assignment.Left))
          continue;

        var fromAssignment = TryReadCraftingStationValue(assignment.Right);
        if (!string.IsNullOrWhiteSpace(fromAssignment))
          return fromAssignment;
      }
    }

    return null;
  }

  private static bool IsCraftingStationTarget(ExpressionSyntax expression)
  {
    return expression switch
    {
      IdentifierNameSyntax identifier => identifier.Identifier.Text == "craftingStation",
      MemberAccessExpressionSyntax memberAccess
        when memberAccess.Name.Identifier.Text == "craftingStation"
          && (memberAccess.Expression is ThisExpressionSyntax || memberAccess.Expression is IdentifierNameSyntax) =>
        true,
      _ => false,
    };
  }

  private static string? TryReadCraftingStationValue(ExpressionSyntax? expression)
  {
    if (expression is null)
      return null;

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

    return
      cursor is MemberAccessExpressionSyntax memberAccess
      && memberAccess.Expression is IdentifierNameSyntax owner
      && owner.Identifier.Text == "CraftingStation"
      ? memberAccess.Name.Identifier.Text
      : null;
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

  private static IReadOnlyList<string> TryReadStringArrayArg(InvocationExpressionSyntax? invocation, int argumentIndex)
  {
    if (invocation is null || invocation.ArgumentList.Arguments.Count <= argumentIndex)
      return [];

    var expression = invocation.ArgumentList.Arguments[argumentIndex].Expression;
    InitializerExpressionSyntax? initializer = expression switch
    {
      ArrayCreationExpressionSyntax arrayCreation => arrayCreation.Initializer,
      ImplicitArrayCreationExpressionSyntax implicitArrayCreation => implicitArrayCreation.Initializer,
      _ => null,
    };

    if (initializer is null)
      return [];

    return initializer
      .Expressions.Select(TryParseLiteralLikeValue)
      .OfType<string>()
      .Where(value => !string.IsNullOrWhiteSpace(value))
      .ToArray();
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
      InvocationExpressionSyntax invocation when SyntaxParsingHelpers.GetInvocationName(invocation) == "nameof" =>
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
