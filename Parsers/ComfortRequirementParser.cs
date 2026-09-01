using Allumeria.Comfort;
using Allumeria.Comfort.Requirements;

internal class ComfortRequirementEntry : Dictionary<string, object?>
{
  public string? Id => TryGetValue("id", out var val) ? (string?)val : null;

  public ComfortRequirementEntry(ComfortRequirement requirement)
  {
    this["name"] = requirement.GetName();

    if (requirement is BlockLightRequirement lightReq)
    {
      this["minLight"] = lightReq.minLight;
      this["maxLight"] = lightReq.maxLight;
    }
    else if (requirement is CraftingStationRequirement craftingReq)
    {
      var stations = new List<string>();

      Reflection.GetPrivate<bool>(craftingReq, "furnace", out var furnace);
      if (furnace)
        stations.Add("furnace");

      Reflection.GetPrivate<bool>(craftingReq, "workbench", out var workbench);
      if (workbench)
        stations.Add("workbench");

      Reflection.GetPrivate<bool>(craftingReq, "decoration", out var decoration);
      if (decoration)
        stations.Add("decoration");

      this["stations"] = stations;
    }
    else if (requirement is RoomSizeRequirement roomSizeReq)
    {
      this["minSize"] = roomSizeReq.minSize;
      this["maxSize"] = roomSizeReq.maxSize;
    }
    else if (requirement is SunLightRequirement sunLightReq)
    {
      this["minLight"] = sunLightReq.minLight;
      this["maxLight"] = sunLightReq.maxLight;
      this["directLight"] = sunLightReq.directLight;
    }
  }
}

internal class ComfortRequirementList : Dictionary<string, object?>
{
  public string? Id => TryGetValue("id", out var val) ? (string?)val : null;

  public ComfortRequirementList(List<ComfortRequirement> requirements, string id)
  {
    this["id"] = id;

    this["requirements"] = requirements.Select(r => new ComfortRequirementEntry(r)).ToArray();
  }
}

internal static class ComfortRequirementParser
{
  public static Dictionary<List<ComfortRequirement>, ComfortRequirementList> entries = [];

  public static Dictionary<List<ComfortRequirement>, ComfortRequirementList> Parse()
  {
    var requirementMap = Reflection.BuildStaticInstanceNameMap<List<ComfortRequirement>, ComfortCalculator>();

    foreach (var (requirements, id) in requirementMap)
      entries[requirements] = new ComfortRequirementList(requirements, id);

    return entries;
  }
}
