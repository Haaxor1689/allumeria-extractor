using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SpawnParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var entriesById = BuildEntriesById(sourceRoot);
    var result = new List<object>();

    foreach (var pair in entriesById)
    {
      result.Add(
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = pair.Key, ["entries"] = pair.Value }
      );
    }

    return result;
  }

  public static IReadOnlyDictionary<string, string> BuildMonsterSpawnLookup(string sourceRoot)
  {
    var entriesById = BuildEntriesById(sourceRoot);
    var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var pair in entriesById)
    {
      var spawnId = pair.Key;

      foreach (var entry in pair.Value)
      {
        if (entry is not Dictionary<string, object?> dictionary)
          continue;

        if (!dictionary.TryGetValue("monster", out var monsterValue))
          continue;

        var monster = monsterValue?.ToString();
        if (string.IsNullOrWhiteSpace(monster))
          continue;

        if (!lookup.ContainsKey(monster))
          lookup[monster] = spawnId;
      }
    }

    return lookup;
  }

  private static IReadOnlyDictionary<string, List<object>> BuildEntriesById(string sourceRoot)
  {
    var path = Path.Combine(sourceRoot, "EntitySystem", "Spawning", "SpawnDefinition.cs");
    if (!File.Exists(path))
      return new Dictionary<string, List<object>>(StringComparer.Ordinal);

    var root = SyntaxParsingHelpers.ParseCompilationUnit(path);
    var byId = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

    foreach (var field in SyntaxParsingHelpers.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var id = variable.Identifier.Text;
        if (string.IsNullOrWhiteSpace(id))
          continue;

        var list = GetOrCreate(byId, id);
        AppendSpawnMonsters(list, variable.Initializer?.Value);
      }
    }

    var initMethod = root.DescendantNodes()
      .OfType<MethodDeclarationSyntax>()
      .FirstOrDefault(method => method.Identifier.Text == "InitSpawnDefinitions");

    if (initMethod?.Body is not null)
    {
      foreach (var invocation in initMethod.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
      {
        if (SyntaxParsingHelpers.GetInvocationName(invocation) != "AddEntry")
          continue;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
          continue;

        var targetId = TryReadSpawnDefinitionMemberName(memberAccess.Expression);
        if (string.IsNullOrWhiteSpace(targetId))
          continue;

        var list = GetOrCreate(byId, targetId);
        if (invocation.ArgumentList.Arguments.Count == 0)
          continue;

        AppendSpawnMonsters(list, invocation.ArgumentList.Arguments[0].Expression);
      }
    }

    return byId;
  }

  private static List<object> GetOrCreate(IDictionary<string, List<object>> map, string id)
  {
    if (!map.TryGetValue(id, out var list))
    {
      list = new List<object>();
      map[id] = list;
    }

    return list;
  }

  private static void AppendSpawnMonsters(ICollection<object> list, ExpressionSyntax? expression)
  {
    if (expression is null)
      return;

    foreach (var creation in expression.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>())
    {
      if (NormalizeTypeName(creation.Type.ToString()) != "SpawnMonster")
        continue;

      var monster = TryReadMonsterType(creation);
      if (string.IsNullOrWhiteSpace(monster))
        continue;

      var entry = new Dictionary<string, object?>(StringComparer.Ordinal) { ["monster"] = monster };
      var loot = TryReadLoot(creation);
      if (!string.IsNullOrWhiteSpace(loot))
        entry["loot"] = loot;

      list.Add(entry);
    }
  }

  private static string? TryReadMonsterType(ObjectCreationExpressionSyntax spawnMonsterCreation)
  {
    var args = spawnMonsterCreation.ArgumentList?.Arguments;
    if (args is null || args.Value.Count == 0)
      return null;

    var reduced = Unwrap(args.Value[0].Expression);
    if (reduced is not TypeOfExpressionSyntax typeOfExpression)
      return null;

    return NormalizeTypeName(typeOfExpression.Type.ToString());
  }

  private static string? TryReadLoot(ObjectCreationExpressionSyntax spawnMonsterCreation)
  {
    var args = spawnMonsterCreation.ArgumentList?.Arguments;
    if (args is null || args.Value.Count < 2)
      return null;

    var reduced = Unwrap(args.Value[1].Expression);
    return reduced switch
    {
      MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      _ => null,
    };
  }

  private static string? TryReadSpawnDefinitionMemberName(ExpressionSyntax expression)
  {
    var reduced = Unwrap(expression);
    return reduced switch
    {
      MemberAccessExpressionSyntax memberAccess
        when NormalizeTypeName(memberAccess.Expression.ToString()) == "SpawnDefinition" => memberAccess
        .Name
        .Identifier
        .Text,
      _ => null,
    };
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
}
