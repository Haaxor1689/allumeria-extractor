using System.Reflection;
using Allumeria.EntitySystem.Spawning;

internal class ExportedSpawnEntry : Dictionary<string, object?>
{
  public string Id => (string)this["id"]!;

  public ExportedSpawnEntry(SpawnEntry spawn, string id)
  {
    this["id"] = id;

    this["entries"] = BuildSpawnEntries(spawn);
  }

  private static object BuildSpawnEntries(SpawnEntry spawn)
  {
    if (spawn == null)
      return new List<object>();

    var type = spawn.GetType();

    if (spawn is SpawnMonster spawnMonster)
    {
      var result = new Dictionary<string, object?>();

      var entityTypeField = type.GetField("entityType", BindingFlags.NonPublic | BindingFlags.Instance);
      if (entityTypeField?.GetValue(spawnMonster) is Type entityType)
        result["monster"] = entityType.Name;

      var overrideLootField = type.GetField("overrideLoot", BindingFlags.NonPublic | BindingFlags.Instance);
      if (overrideLootField?.GetValue(spawnMonster) is Allumeria.Items.LootTables.LootDescription lootDescription)
      {
        var strIdField = lootDescription.GetType().GetField("strID", BindingFlags.NonPublic | BindingFlags.Instance);
        if (strIdField?.GetValue(lootDescription) is string lootStrId)
          result["loot"] = lootStrId;
      }

      return result;
    }

    var childEntries = spawn.childEntries.Select(child => BuildSpawnEntries(child)).ToList();
    if (childEntries.Count == 1)
      return childEntries[0];

    // TODO: Handle SpawnChoose and other spawn types if needed
    // if (spawn is SpawnChoose spawnChoose)
    //   return new { oneOf = childEntries };
    // else
    return childEntries;
  }
}

internal static class SpawnParser
{
  public static Dictionary<SpawnEntry, ExportedSpawnEntry> entries = [];

  public static Dictionary<SpawnEntry, ExportedSpawnEntry> Parse()
  {
    // SpawnDefinition.InitSpawnDefinitions();

    var spawnMap = ReflectionHelpers.BuildStaticInstanceNameMap<SpawnEntry, SpawnDefinition>();

    foreach (var (entry, id) in spawnMap)
      entries[entry] = new ExportedSpawnEntry(entry, id);

    return entries;
  }
}
