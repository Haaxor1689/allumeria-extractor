using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class EntityParser
{
  private static readonly HashSet<string> SkippedEntityTypes = new(StringComparer.OrdinalIgnoreCase)
  {
    "PlaceholderEntity",
    "PlayerEntity",
    "TestEntity",
    "BossTest",
    "ItemEntity",
    "LivingEntity",
    "ProjectileEntity",
    "MinecartEntity",
  };

  private static readonly string[] LivingFieldOrder =
  [
    "walkSpeed",
    "canSpawnInSunlight",
    "health",
    "defence",
    "baseDamage",
    "minCoinDrop",
    "maxCoinDrop",
    "scale",
    "model",
    "loot",
    "flying",
  ];

  private static readonly IReadOnlyDictionary<string, object?> LivingFieldDefaults = new Dictionary<string, object?>(
    StringComparer.Ordinal
  )
  {
    ["walkSpeed"] = 0.1f,
    ["canSpawnInSunlight"] = false,
    ["health"] = 20,
    ["defence"] = 0,
    ["baseDamage"] = 5,
    ["minCoinDrop"] = 1,
    ["maxCoinDrop"] = 5,
    ["scale"] = new[] { 1f, 1f, 1f },
    ["model"] = null,
    ["loot"] = null,
  };

  private static readonly HashSet<string> LivingFieldNameSet = new(LivingFieldOrder, StringComparer.Ordinal);

  public static List<object> Parse(string sourceRoot)
  {
    var entityRootPath = Path.Combine(sourceRoot, "EntitySystem", "Entity.cs");
    if (!File.Exists(entityRootPath))
      return [];

    var metadataByType = BuildEntityMetadataMap(sourceRoot);
    var spawnByMonster = SpawnParser.BuildMonsterSpawnLookup(sourceRoot);

    var root = SyntaxParsingUtils.ParseCompilationUnit(entityRootPath);
    var registerMethod = root.DescendantNodes()
      .OfType<MethodDeclarationSyntax>()
      .FirstOrDefault(method => method.Identifier.Text == "RegisterEntities");

    if (registerMethod is null)
      return [];

    var entries = new List<object>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var invocation in registerMethod.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
      if (SyntaxParsingUtils.GetInvocationName(invocation) != "RegisterEntityType")
        continue;

      if (invocation.ArgumentList.Arguments.Count == 0)
        continue;

      var typeName = TryReadTypeOfName(invocation.ArgumentList.Arguments[0].Expression);
      if (string.IsNullOrWhiteSpace(typeName))
        continue;

      if (SkippedEntityTypes.Contains(typeName))
        continue;

      if (!seen.Add(typeName))
        continue;

      var entry = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = typeName };

      if (IsLivingEntityType(typeName, metadataByType))
      {
        var isBoss =
          metadataByType.TryGetValue(typeName, out var livingMeta)
          && string.Equals(livingMeta.Category, "Bosses", StringComparison.Ordinal);
        entry["category"] = isBoss ? "boss" : "creature";

        if (spawnByMonster.TryGetValue(typeName, out var spawnId) && !string.IsNullOrWhiteSpace(spawnId))
          entry["spawn"] = spawnId;

        var livingFields = ResolveLivingFieldValues(typeName, metadataByType);

        // If the model was supplied via a constructor parameter, resolve actual values from all call sites
        var modelParamIndex = FindModelParamIndexInChain(typeName, metadataByType);
        if (modelParamIndex.HasValue)
        {
          var callSiteModels = CollectModelsFromCallSites(typeName, modelParamIndex.Value, sourceRoot);
          if (callSiteModels.Length > 0)
          {
            var mutableFields = new Dictionary<string, object?>(livingFields, StringComparer.Ordinal);
            mutableFields["model"] = callSiteModels;
            livingFields = mutableFields;
          }
        }

        foreach (var fieldName in LivingFieldOrder)
        {
          if (livingFields.TryGetValue(fieldName, out var value) && !IsEmptyLivingFieldValue(fieldName, value))
            entry[fieldName] = value;
        }

        if (
          livingFields.TryGetValue("texture", out var textureVal)
          && textureVal is string textureStr
          && !string.IsNullOrWhiteSpace(textureStr)
        )
          entry["texture"] = textureStr;
      }
      else if (IsProjectileEntityType(typeName, metadataByType))
      {
        entry["category"] = "projectile";

        var projFields = ResolveProjectileFieldValues(typeName, metadataByType);

        if (
          projFields.TryGetValue("model", out var projModel)
          && projModel is string projModelStr
          && !string.IsNullOrWhiteSpace(projModelStr)
        )
          entry["model"] = projModelStr;

        if (
          projFields.TryGetValue("texture", out var projTexture)
          && projTexture is string projTextureStr
          && !string.IsNullOrWhiteSpace(projTextureStr)
        )
          entry["texture"] = projTextureStr;

        if (
          projFields.TryGetValue("maxTicks", out var maxTicksRaw)
          && TryConvertToInt(maxTicksRaw) is int maxTicks
          && maxTicks != 600
        )
          entry["maxTicks"] = maxTicks;

        if (
          projFields.TryGetValue("hasGravity", out var hasGravityRaw)
          && hasGravityRaw is bool hasGravity
          && !hasGravity
        )
          entry["hasGravity"] = false;
      }
      else if (IsVehicleEntityType(typeName, metadataByType))
      {
        entry["category"] = "vehicle";

        var vehicleFields = ResolveProjectileFieldValues(typeName, metadataByType);
        if (
          vehicleFields.TryGetValue("model", out var vehicleModel)
          && vehicleModel is string vehicleModelStr
          && !string.IsNullOrWhiteSpace(vehicleModelStr)
        )
          entry["model"] = vehicleModelStr;

        if (
          vehicleFields.TryGetValue("texture", out var vehicleTexture)
          && vehicleTexture is string vehicleTextureStr
          && !string.IsNullOrWhiteSpace(vehicleTextureStr)
        )
          entry["texture"] = vehicleTextureStr;
      }
      else
      {
        continue;
      }

      entries.Add(entry);
    }

    return entries;
  }

  private static IReadOnlyDictionary<string, EntityTypeMetadata> BuildEntityMetadataMap(string sourceRoot)
  {
    var map = new Dictionary<string, EntityTypeMetadata>(StringComparer.OrdinalIgnoreCase);
    var entitiesRoot = Path.Combine(sourceRoot, "EntitySystem", "Entities");

    if (!Directory.Exists(entitiesRoot))
      return map;

    foreach (var file in Directory.EnumerateFiles(entitiesRoot, "*.cs", SearchOption.AllDirectories))
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);

      foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
      {
        var className = declaration.Identifier.Text;
        if (string.IsNullOrWhiteSpace(className))
          continue;

        var namespaceName = TryReadNamespace(declaration);
        var category = TryReadEntityCategory(namespaceName);
        var baseType = declaration.BaseList?.Types.FirstOrDefault()?.Type is { } baseExpression
          ? NormalizeTypeName(baseExpression.ToString())
          : null;

        var ownLivingFieldValues = ReadOwnLivingFieldValues(declaration);
        var ownProjectileFieldValues = ReadOwnProjectileFieldValues(declaration);
        var modelParamIndex = FindModelConstructorParamIndex(declaration);

        map[className] = new EntityTypeMetadata(
          namespaceName,
          category,
          baseType,
          ownLivingFieldValues,
          ownProjectileFieldValues,
          modelParamIndex
        );
      }
    }

    return map;
  }

  private static bool IsLivingEntityType(
    string typeName,
    IReadOnlyDictionary<string, EntityTypeMetadata> metadataByType
  )
  {
    var cursor = typeName;

    while (!string.IsNullOrWhiteSpace(cursor))
    {
      if (string.Equals(cursor, "LivingEntity", StringComparison.Ordinal))
        return true;

      if (!metadataByType.TryGetValue(cursor, out var metadata) || string.IsNullOrWhiteSpace(metadata.BaseType))
        return false;

      cursor = metadata.BaseType;
    }

    return false;
  }

  private static bool IsProjectileEntityType(
    string typeName,
    IReadOnlyDictionary<string, EntityTypeMetadata> metadataByType
  )
  {
    var cursor = typeName;

    while (!string.IsNullOrWhiteSpace(cursor))
    {
      if (string.Equals(cursor, "ProjectileEntity", StringComparison.Ordinal))
        return true;

      if (!metadataByType.TryGetValue(cursor, out var metadata) || string.IsNullOrWhiteSpace(metadata.BaseType))
        return false;

      cursor = metadata.BaseType;
    }

    return false;
  }

  private static bool IsVehicleEntityType(
    string typeName,
    IReadOnlyDictionary<string, EntityTypeMetadata> metadataByType
  )
  {
    var cursor = typeName;

    while (!string.IsNullOrWhiteSpace(cursor))
    {
      if (string.Equals(cursor, "MinecartEntity", StringComparison.Ordinal))
        return true;

      if (!metadataByType.TryGetValue(cursor, out var metadata) || string.IsNullOrWhiteSpace(metadata.BaseType))
        return false;

      cursor = metadata.BaseType;
    }

    return false;
  }

  private static IReadOnlyDictionary<string, object?> ResolveLivingFieldValues(
    string typeName,
    IReadOnlyDictionary<string, EntityTypeMetadata> metadataByType
  )
  {
    var chain = BuildInheritanceChain(typeName, metadataByType);
    var values = new Dictionary<string, object?>(LivingFieldDefaults, StringComparer.Ordinal);

    foreach (var chainType in chain)
    {
      if (!metadataByType.TryGetValue(chainType, out var metadata))
        continue;

      foreach (var pair in metadata.OwnLivingFieldValues)
        values[pair.Key] = pair.Value;
    }

    return values;
  }

  private static IReadOnlyDictionary<string, object?> ResolveProjectileFieldValues(
    string typeName,
    IReadOnlyDictionary<string, EntityTypeMetadata> metadataByType
  )
  {
    var chain = BuildInheritanceChain(typeName, metadataByType);
    var values = new Dictionary<string, object?>(StringComparer.Ordinal);

    foreach (var chainType in chain)
    {
      if (!metadataByType.TryGetValue(chainType, out var metadata))
        continue;

      foreach (var pair in metadata.OwnProjectileFieldValues)
        values[pair.Key] = pair.Value;
    }

    return values;
  }

  private static IReadOnlyList<string> BuildInheritanceChain(
    string typeName,
    IReadOnlyDictionary<string, EntityTypeMetadata> metadataByType
  )
  {
    var reversed = new List<string>();
    var cursor = typeName;

    while (!string.IsNullOrWhiteSpace(cursor))
    {
      reversed.Add(cursor);

      if (!metadataByType.TryGetValue(cursor, out var metadata) || string.IsNullOrWhiteSpace(metadata.BaseType))
        break;

      cursor = metadata.BaseType;
    }

    reversed.Reverse();
    return reversed;
  }

  private static IReadOnlyDictionary<string, object?> ReadOwnLivingFieldValues(ClassDeclarationSyntax declaration)
  {
    var values = new Dictionary<string, object?>(StringComparer.Ordinal);

    foreach (var field in declaration.Members.OfType<FieldDeclarationSyntax>())
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var fieldName = variable.Identifier.Text;
        if (!LivingFieldNameSet.Contains(fieldName))
          continue;

        if (variable.Initializer?.Value is not ExpressionSyntax initializer)
          continue;

        var parsed = TryParseLiteralLikeValue(initializer);
        if (parsed is null)
          continue;

        values[fieldName] = parsed;
      }
    }

    foreach (var constructor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
      MergeAssignmentOverrides(values, constructor.Body);

    foreach (
      var method in declaration.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.Text == "InitValues")
    )
      MergeAssignmentOverrides(values, method.Body);

    foreach (var constructor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
      MergeModelOverrides(values, constructor.Body);

    foreach (
      var method in declaration.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.Text == "InitValues")
    )
      MergeModelOverrides(values, method.Body);

    return values;
  }

  private static IReadOnlyDictionary<string, object?> ReadOwnProjectileFieldValues(ClassDeclarationSyntax declaration)
  {
    var values = new Dictionary<string, object?>(StringComparer.Ordinal);

    foreach (var field in declaration.Members.OfType<FieldDeclarationSyntax>())
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var fieldName = variable.Identifier.Text;
        if (fieldName is not ("maxTicks" or "baseDamage"))
          continue;

        if (variable.Initializer?.Value is not ExpressionSyntax initializer)
          continue;

        var parsed = TryParseLiteralLikeValue(initializer);
        if (parsed is null)
          continue;

        values[fieldName] = parsed;
      }
    }

    var bodies = declaration
      .Members.OfType<ConstructorDeclarationSyntax>()
      .Select(c => c.Body)
      .Concat(
        declaration
          .Members.OfType<MethodDeclarationSyntax>()
          .Where(m => m.Identifier.Text is "Setup" or "Setup2" or "InitValues" or "InitComponents")
          .Select(m => m.Body)
      );

    foreach (var body in bodies)
      MergeProjectileOverrides(values, body);

    return values;
  }

  private static void MergeProjectileOverrides(IDictionary<string, object?> values, BlockSyntax? body)
  {
    if (body is null)
      return;

    foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
    {
      // this.phys.hasGravity = <bool>
      if (
        assignment.Left is MemberAccessExpressionSyntax hasGravityLeft
        && hasGravityLeft.Name.Identifier.Text == "hasGravity"
        && hasGravityLeft.Expression is MemberAccessExpressionSyntax physLeft
        && physLeft.Expression is ThisExpressionSyntax
        && physLeft.Name.Identifier.Text == "phys"
      )
      {
        if (TryParseLiteralLikeValue(assignment.Right) is bool hasGravity)
          values["hasGravity"] = hasGravity;
        continue;
      }

      var fieldName = TryReadAssignedFieldName(assignment.Left);
      if (string.IsNullOrWhiteSpace(fieldName) || fieldName is not ("maxTicks" or "baseDamage"))
        continue;

      var parsed = TryParseLiteralLikeValue(assignment.Right);
      if (parsed is null)
        continue;

      values[fieldName] = parsed;
    }

    foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
      if (SyntaxParsingUtils.GetInvocationName(invocation) != "SetModel")
        continue;

      if (
        invocation.Expression is not MemberAccessExpressionSyntax memberAccess
        || memberAccess.Expression is not MemberAccessExpressionSyntax modelMember
        || modelMember.Expression is not ThisExpressionSyntax
        || modelMember.Name.Identifier.Text != "model"
      )
      {
        continue;
      }

      var model = TryReadAssetModelKey(invocation, 0);
      if (!string.IsNullOrWhiteSpace(model))
        values["model"] = model;

      var texture = TryReadAssetTextureKey(invocation, 1);
      if (!string.IsNullOrWhiteSpace(texture))
        values["texture"] = texture;
    }
  }

  private static void MergeAssignmentOverrides(IDictionary<string, object?> values, BlockSyntax? body)
  {
    if (body is null)
      return;

    foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
    {
      var flyingValue = TryReadFlyingAssignmentValue(assignment);
      if (flyingValue.HasValue)
      {
        if (flyingValue.Value)
          values["flying"] = true;

        continue;
      }

      var healthValue = TryReadHealthAssignmentValue(assignment);
      if (healthValue.HasValue)
      {
        values["health"] = healthValue.Value;
        continue;
      }

      var defenceValue = TryReadDefenceAssignmentValue(assignment);
      if (defenceValue.HasValue)
      {
        values["defence"] = defenceValue.Value;
        continue;
      }

      var fieldName = TryReadAssignedFieldName(assignment.Left);
      if (string.IsNullOrWhiteSpace(fieldName) || !LivingFieldNameSet.Contains(fieldName))
        continue;

      var parsed = TryParseLiteralLikeValue(assignment.Right);
      if (parsed is null)
        continue;

      values[fieldName] = parsed;
    }
  }

  private static int? TryReadHealthAssignmentValue(AssignmentExpressionSyntax assignment)
  {
    if (assignment.Left is not MemberAccessExpressionSyntax leftMember)
      return null;

    if (
      leftMember.Expression is not MemberAccessExpressionSyntax healthMember
      || healthMember.Expression is not ThisExpressionSyntax
      || healthMember.Name.Identifier.Text != "health"
    )
    {
      return null;
    }

    var healthFieldName = leftMember.Name.Identifier.Text;
    if (!string.Equals(healthFieldName, "maxHealth", StringComparison.Ordinal))
      return null;

    return TryConvertToInt(TryParseLiteralLikeValue(assignment.Right));
  }

  private static int? TryReadDefenceAssignmentValue(AssignmentExpressionSyntax assignment)
  {
    if (assignment.Left is not MemberAccessExpressionSyntax leftMember)
      return null;

    if (
      leftMember.Expression is not MemberAccessExpressionSyntax healthMember
      || healthMember.Expression is not ThisExpressionSyntax
      || healthMember.Name.Identifier.Text != "health"
    )
    {
      return null;
    }

    var healthFieldName = leftMember.Name.Identifier.Text;
    if (!string.Equals(healthFieldName, "defence", StringComparison.Ordinal))
      return null;

    return TryConvertToInt(TryParseLiteralLikeValue(assignment.Right));
  }

  private static bool? TryReadFlyingAssignmentValue(AssignmentExpressionSyntax assignment)
  {
    if (assignment.Left is not MemberAccessExpressionSyntax leftMember)
      return null;

    if (
      leftMember.Expression is not MemberAccessExpressionSyntax physMember
      || physMember.Expression is not ThisExpressionSyntax
      || physMember.Name.Identifier.Text != "phys"
    )
    {
      return null;
    }

    if (!string.Equals(leftMember.Name.Identifier.Text, "gravity", StringComparison.Ordinal))
      return null;

    return IsVector3ZeroExpression(assignment.Right);
  }

  private static bool IsVector3ZeroExpression(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);

    return reduced is MemberAccessExpressionSyntax memberAccess
      && NormalizeTypeName(memberAccess.Expression.ToString()) == "Vector3"
      && memberAccess.Name.Identifier.Text == "Zero";
  }

  private static void MergeModelOverrides(IDictionary<string, object?> values, BlockSyntax? body)
  {
    if (body is null)
      return;

    foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
      if (SyntaxParsingUtils.GetInvocationName(invocation) != "SetModel")
        continue;

      if (
        invocation.Expression is not MemberAccessExpressionSyntax memberAccess
        || memberAccess.Expression is not MemberAccessExpressionSyntax modelMember
        || modelMember.Expression is not ThisExpressionSyntax
        || modelMember.Name.Identifier.Text != "model"
      )
      {
        continue;
      }

      var model = TryReadAssetModelKey(invocation, 0);
      if (!string.IsNullOrWhiteSpace(model))
        values["model"] = model;

      var texture = TryReadAssetTextureKey(invocation, 1);
      if (!string.IsNullOrWhiteSpace(texture))
        values["texture"] = texture;
    }
  }

  private static string? TryReadAssetModelKey(InvocationExpressionSyntax invocation, int argumentIndex)
  {
    if (invocation.ArgumentList.Arguments.Count <= argumentIndex)
      return null;

    var argumentExpression = Unwrap(invocation.ArgumentList.Arguments[argumentIndex].Expression);
    if (argumentExpression is not InvocationExpressionSyntax argumentInvocation)
      return null;

    if (SyntaxParsingUtils.GetInvocationName(argumentInvocation) != "GetModel")
      return null;

    if (argumentInvocation.ArgumentList.Arguments.Count == 0)
      return null;

    return TryParseLiteralLikeValue(argumentInvocation.ArgumentList.Arguments[0].Expression) as string;
  }

  private static string? TryReadAssetTextureKey(InvocationExpressionSyntax invocation, int argumentIndex)
  {
    if (invocation.ArgumentList.Arguments.Count <= argumentIndex)
      return null;

    var argumentExpression = Unwrap(invocation.ArgumentList.Arguments[argumentIndex].Expression);
    if (argumentExpression is not InvocationExpressionSyntax argumentInvocation)
      return null;

    if (SyntaxParsingUtils.GetInvocationName(argumentInvocation) != "GetTexture")
      return null;

    if (argumentInvocation.ArgumentList.Arguments.Count == 0)
      return null;

    return TryParseLiteralLikeValue(argumentInvocation.ArgumentList.Arguments[0].Expression) as string;
  }

  private static string? TryReadAssignedFieldName(ExpressionSyntax left)
  {
    var reduced = Unwrap(left);

    return reduced switch
    {
      MemberAccessExpressionSyntax memberAccess when memberAccess.Expression is ThisExpressionSyntax => memberAccess
        .Name
        .Identifier
        .Text,
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      _ => null,
    };
  }

  private static string? TryReadTypeOfName(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);
    if (reduced is not TypeOfExpressionSyntax typeOfExpression)
      return null;

    return NormalizeTypeName(typeOfExpression.Type.ToString());
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

  private static string? TryReadNamespace(ClassDeclarationSyntax declaration)
  {
    SyntaxNode? cursor = declaration.Parent;
    while (cursor is not null)
    {
      switch (cursor)
      {
        case NamespaceDeclarationSyntax ns:
          return ns.Name.ToString().Trim();
        case FileScopedNamespaceDeclarationSyntax fileScopedNs:
          return fileScopedNs.Name.ToString().Trim();
        default:
          cursor = cursor.Parent;
          break;
      }
    }

    return null;
  }

  private static string? TryReadEntityCategory(string? namespaceName)
  {
    if (string.IsNullOrWhiteSpace(namespaceName))
      return null;

    const string prefix = "Allumeria.EntitySystem.Entities";
    if (!namespaceName.StartsWith(prefix, StringComparison.Ordinal))
      return null;

    if (namespaceName.Length == prefix.Length)
      return "Core";

    if (namespaceName.Length <= prefix.Length + 1 || namespaceName[prefix.Length] != '.')
      return null;

    return namespaceName[(prefix.Length + 1)..];
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

    if (reduced is ObjectCreationExpressionSyntax creation && NormalizeTypeName(creation.Type.ToString()) == "Vector3")
    {
      var args = creation.ArgumentList?.Arguments;
      if (args is null || args.Value.Count != 3)
        return null;

      var x = TryConvertToFloat(TryParseLiteralLikeValue(args.Value[0].Expression));
      var y = TryConvertToFloat(TryParseLiteralLikeValue(args.Value[1].Expression));
      var z = TryConvertToFloat(TryParseLiteralLikeValue(args.Value[2].Expression));

      if (!x.HasValue || !y.HasValue || !z.HasValue)
        return null;

      return new[] { x.Value, y.Value, z.Value };
    }

    return reduced switch
    {
      LiteralExpressionSyntax literal when literal.Token.Value is not null => literal.Token.Value,
      MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
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

  private static bool IsEmptyLivingFieldValue(string fieldName, object? value)
  {
    return fieldName switch
    {
      "canSpawnInSunlight" => value is bool boolValue && !boolValue,
      "defence" => TryConvertToInt(value) == 0,
      "scale" => IsDefaultScale(value),
      "loot" => value is null,
      _ => false,
    };
  }

  private static bool IsDefaultScale(object? value)
  {
    if (value is not float[] scale || scale.Length != 3)
      return false;

    return scale[0] == 1f && scale[1] == 1f && scale[2] == 1f;
  }

  /// <summary>
  /// Returns the index of the constructor parameter that is passed directly to
  /// <c>AssetManager.GetModel(...)</c> inside a <c>this.model.SetModel</c> call, if any.
  /// </summary>
  private static int? FindModelConstructorParamIndex(ClassDeclarationSyntax declaration)
  {
    foreach (var ctor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
    {
      foreach (var invocation in ctor.DescendantNodes().OfType<InvocationExpressionSyntax>())
      {
        if (SyntaxParsingUtils.GetInvocationName(invocation) != "SetModel")
          continue;

        if (invocation.ArgumentList.Arguments.Count == 0)
          continue;

        var modelArg = Unwrap(invocation.ArgumentList.Arguments[0].Expression);
        if (modelArg is not InvocationExpressionSyntax getModelCall)
          continue;

        if (SyntaxParsingUtils.GetInvocationName(getModelCall) != "GetModel")
          continue;

        if (getModelCall.ArgumentList.Arguments.Count == 0)
          continue;

        var innerArg = Unwrap(getModelCall.ArgumentList.Arguments[0].Expression);
        if (innerArg is not IdentifierNameSyntax paramId)
          continue;

        var paramName = paramId.Identifier.Text;
        var ctorParams = ctor.ParameterList.Parameters;
        for (var i = 0; i < ctorParams.Count; i++)
        {
          if (ctorParams[i].Identifier.Text == paramName)
            return i;
        }
      }
    }

    return null;
  }

  private static int? FindModelParamIndexInChain(
    string typeName,
    IReadOnlyDictionary<string, EntityTypeMetadata> metadataByType
  )
  {
    var chain = BuildInheritanceChain(typeName, metadataByType);
    foreach (var t in chain)
    {
      if (metadataByType.TryGetValue(t, out var meta) && meta.ModelParamIndex.HasValue)
        return meta.ModelParamIndex;
    }

    return null;
  }

  private static string[] CollectModelsFromCallSites(string typeName, int paramIndex, string sourceRoot)
  {
    var models = new List<string>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);
      foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
      {
        if (NormalizeTypeName(creation.Type.ToString()) != typeName)
          continue;

        if (creation.ArgumentList is null)
          continue;

        var args = creation.ArgumentList.Arguments;
        if (args.Count <= paramIndex)
          continue;

        if (
          TryParseLiteralLikeValue(Unwrap(args[paramIndex].Expression)) is string literal
          && literal.Contains('.')
          && seen.Add(literal)
        )
          models.Add(literal);
      }
    }

    return [.. models];
  }

  private sealed record EntityTypeMetadata(
    string? Namespace,
    string? Category,
    string? BaseType,
    IReadOnlyDictionary<string, object?> OwnLivingFieldValues,
    IReadOnlyDictionary<string, object?> OwnProjectileFieldValues,
    int? ModelParamIndex
  );
}
