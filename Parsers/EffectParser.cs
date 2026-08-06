using Allumeria.EntitySystem.Effects;

internal class EffectEntry : Dictionary<string, object?>
{
  public string? Id => TryGetValue("id", out var val) ? (string?)val : null;

  public int? TextureX => TryGetValue("textureX", out var val) ? (int?)val : null;

  public int? TextureY => TryGetValue("textureY", out var val) ? (int?)val : null;

  public EffectEntry(Effect effect)
  {
    var additionalFields = Reflection.GetFieldsAsDict(
      effect,
      ["strID", "intID", "textureX", "textureY", "type", "translatedName"]
    );

    this["id"] = effect.strID;

    this["intId"] = effect.intID;

    var className = effect.GetType().Name;
    if (className != "Effect")
      this["class"] = className[6..];

    this["effectType"] = effect.type.ToString();

    this["textureX"] = effect.textureX;

    this["textureY"] = effect.textureY;

    foreach (var kvp in additionalFields)
      this[kvp.Key] = kvp.Value;
  }
}

internal static class EffectParser
{
  public static Dictionary<Effect, EffectEntry> entries = [];

  public static Dictionary<Effect, EffectEntry> Parse()
  {
    var effects = Allumeria.EntitySystem.Effects.Effect.effects.Where(e => e != null).ToList();

    foreach (var effect in effects)
      entries[effect] = new EffectEntry(effect);

    return entries;
  }
}
