using System.Reflection;
using Allumeria.EntitySystem.Spawning;
using Allumeria.Items.LootTables;

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
    var type = spawn.GetType();

    if (spawn is SpawnMonster spawnMonster)
    {
      var result = new Dictionary<string, object?>();

      if (Reflection.GetPrivate<Type>(spawnMonster, "entityType", out var entityType))
        result["monster"] = entityType.Name;

      if (Reflection.GetPrivate<LootDescription>(spawnMonster, "overrideLoot", out var lootDescription))
      {
        if (Reflection.GetPrivate<string>(lootDescription, "strID", out var lootStrId))
          result["loot"] = lootStrId;
      }

      return result;
    }

    var childEntries = spawn.childEntries.Select(BuildSpawnEntries).ToList();
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
    SpawnDefinition.InitSpawnDefinitions();

    var spawnMap = Reflection.BuildStaticInstanceNameMap<SpawnEntry, SpawnDefinition>();

    foreach (var (entry, id) in spawnMap)
      entries[entry] = new ExportedSpawnEntry(entry, id);

    return entries;
  }
}
