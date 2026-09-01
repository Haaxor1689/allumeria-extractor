using System.Reflection;
using System.Reflection.Emit;
using Allumeria.EntitySystem;
using Allumeria.EntitySystem.Entities;
using Allumeria.EntitySystem.Entities.Misc;
using Allumeria.EntitySystem.Entities.NPCs;
using Allumeria.EntitySystem.Entities.Walkers;
using OpenTK.Mathematics;

internal class EntityEntry : Dictionary<string, object?>
{
  public string? Model => this.TryGetValue("model", out var modelObj) && modelObj is string model ? model : null;

  public string? Texture =>
    this.TryGetValue("texture", out var textureObj) && textureObj is string texture ? texture : null;

  public EntityEntry(Type entity)
  {
    this["id"] = entity.Name;
    this["category"] = ResolveCategory(entity);

    if (typeof(LivingEntity).IsAssignableFrom(entity))
      AddLivingFields(entity);

    var (model, texture) = Reflection.GetEntityModelTexture(entity);
    if (!string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(texture))
    {
      this["model"] = model;
      this["texture"] = texture;
    }
  }

  private static string ResolveCategory(Type entity)
  {
    if (typeof(ProjectileEntity).IsAssignableFrom(entity))
      return "projectile";

    if (typeof(MinecartEntity).IsAssignableFrom(entity))
      return "vehicle";

    if (typeof(NPCBase).IsAssignableFrom(entity) && entity != typeof(NPCBase))
      return "npc";

    if (typeof(LivingEntity).IsAssignableFrom(entity) && entity != typeof(LivingEntity))
      return "creature";

    return "entity";
  }

  private void AddLivingFields(Type entityType)
  {
    // Defaults come from LivingEntity field initializers / component initialization.
    var walkSpeed = 0.1f;
    var health = 20;
    var defence = 0;
    var baseDamage = 5;
    var minCoinDrop = 1;
    var maxCoinDrop = 5;
    var canSpawnInSunlight = false;
    var flying = false;
    string? loot = null;

    var chain = new List<Type>();
    var cursor = entityType;
    while (cursor != null && cursor != typeof(object))
    {
      chain.Add(cursor);
      cursor = cursor.BaseType;
    }
    chain.Reverse();

    foreach (var type in chain)
    {
      var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

      // Prefer the same constructor shape we previously used for runtime instantiation.
      var preferred = constructors.FirstOrDefault(c =>
      {
        var parameters = c.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == typeof(Vector3);
      });

      if (preferred != null)
      {
        ApplyLivingAssignmentsFromConstructor(
          preferred,
          ref walkSpeed,
          ref health,
          ref defence,
          ref baseDamage,
          ref minCoinDrop,
          ref maxCoinDrop,
          ref canSpawnInSunlight,
          ref flying,
          ref loot
        );
        continue;
      }
    }

    this["walkSpeed"] = walkSpeed;
    this["health"] = health;
    this["defence"] = defence;
    this["baseDamage"] = baseDamage;
    this["minCoinDrop"] = minCoinDrop;
    this["maxCoinDrop"] = maxCoinDrop;
    this["canSpawnInSunlight"] = canSpawnInSunlight;
    this["flying"] = flying;
    if (!string.IsNullOrWhiteSpace(loot))
      this["loot"] = loot;
  }

  private readonly record struct IlStackValue(string? Path, int? IntValue, float? FloatValue)
  {
    public static IlStackValue Unknown => new(null, null, null);
    public static IlStackValue This => new("this", null, null);

    public static IlStackValue FromInt(int value) => new(null, value, null);

    public static IlStackValue FromFloat(float value) => new(null, null, value);

    public bool HasPath(string path) => string.Equals(Path, path, StringComparison.Ordinal);

    public IlStackValue WithPath(string path) => new(path, IntValue, FloatValue);
  }

  private static void ApplyLivingAssignmentsFromConstructor(
    ConstructorInfo ctor,
    ref float walkSpeed,
    ref int health,
    ref int defence,
    ref int baseDamage,
    ref int minCoinDrop,
    ref int maxCoinDrop,
    ref bool canSpawnInSunlight,
    ref bool flying,
    ref string? loot
  )
  {
    var body = ctor.GetMethodBody();
    if (body == null)
      return;

    var il = body.GetILAsByteArray();
    if (il == null || il.Length == 0)
      return;

    var stack = new Stack<IlStackValue>();
    var locals = new Dictionary<int, IlStackValue>();
    var module = ctor.Module;
    var position = 0;

    while (position < il.Length)
    {
      var op = ReadOpCode(il, ref position);

      if (op.Value == OpCodes.Nop.Value) { }
      else if (op.Value == OpCodes.Ldarg_0.Value)
      {
        stack.Push(IlStackValue.This);
      }
      else if (
        op.Value == OpCodes.Ldarg_1.Value
        || op.Value == OpCodes.Ldarg_2.Value
        || op.Value == OpCodes.Ldarg_3.Value
      )
      {
        stack.Push(IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Ldarg_S.Value)
      {
        var argIndex = il[position++];
        stack.Push(argIndex == 0 ? IlStackValue.This : IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Ldarg.Value)
      {
        var argIndex = BitConverter.ToUInt16(il, position);
        position += 2;
        stack.Push(argIndex == 0 ? IlStackValue.This : IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Ldc_I4_M1.Value)
      {
        stack.Push(IlStackValue.FromInt(-1));
      }
      else if (op.Value == OpCodes.Ldc_I4_0.Value)
      {
        stack.Push(IlStackValue.FromInt(0));
      }
      else if (op.Value == OpCodes.Ldc_I4_1.Value)
      {
        stack.Push(IlStackValue.FromInt(1));
      }
      else if (op.Value == OpCodes.Ldc_I4_2.Value)
      {
        stack.Push(IlStackValue.FromInt(2));
      }
      else if (op.Value == OpCodes.Ldc_I4_3.Value)
      {
        stack.Push(IlStackValue.FromInt(3));
      }
      else if (op.Value == OpCodes.Ldc_I4_4.Value)
      {
        stack.Push(IlStackValue.FromInt(4));
      }
      else if (op.Value == OpCodes.Ldc_I4_5.Value)
      {
        stack.Push(IlStackValue.FromInt(5));
      }
      else if (op.Value == OpCodes.Ldc_I4_6.Value)
      {
        stack.Push(IlStackValue.FromInt(6));
      }
      else if (op.Value == OpCodes.Ldc_I4_7.Value)
      {
        stack.Push(IlStackValue.FromInt(7));
      }
      else if (op.Value == OpCodes.Ldc_I4_8.Value)
      {
        stack.Push(IlStackValue.FromInt(8));
      }
      else if (op.Value == OpCodes.Ldc_I4_S.Value)
      {
        stack.Push(IlStackValue.FromInt((sbyte)il[position++]));
      }
      else if (op.Value == OpCodes.Ldc_I4.Value)
      {
        stack.Push(IlStackValue.FromInt(BitConverter.ToInt32(il, position)));
        position += 4;
      }
      else if (op.Value == OpCodes.Ldc_R4.Value)
      {
        stack.Push(IlStackValue.FromFloat(BitConverter.ToSingle(il, position)));
        position += 4;
      }
      else if (op.Value == OpCodes.Conv_R4.Value)
      {
        if (stack.Count == 0)
          continue;

        var source = stack.Pop();
        if (source.FloatValue.HasValue)
          stack.Push(source);
        else if (source.IntValue.HasValue)
          stack.Push(IlStackValue.FromFloat(source.IntValue.Value));
        else
          stack.Push(IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Dup.Value)
      {
        if (stack.Count == 0)
          continue;
        var top = stack.Peek();
        stack.Push(top);
      }
      else if (op.Value == OpCodes.Pop.Value)
      {
        if (stack.Count > 0)
          stack.Pop();
      }
      else if (op.Value == OpCodes.Stloc_0.Value)
      {
        locals[0] = stack.Count > 0 ? stack.Pop() : IlStackValue.Unknown;
      }
      else if (op.Value == OpCodes.Stloc_1.Value)
      {
        locals[1] = stack.Count > 0 ? stack.Pop() : IlStackValue.Unknown;
      }
      else if (op.Value == OpCodes.Stloc_2.Value)
      {
        locals[2] = stack.Count > 0 ? stack.Pop() : IlStackValue.Unknown;
      }
      else if (op.Value == OpCodes.Stloc_3.Value)
      {
        locals[3] = stack.Count > 0 ? stack.Pop() : IlStackValue.Unknown;
      }
      else if (op.Value == OpCodes.Stloc_S.Value)
      {
        var index = il[position++];
        locals[index] = stack.Count > 0 ? stack.Pop() : IlStackValue.Unknown;
      }
      else if (op.Value == OpCodes.Stloc.Value)
      {
        var index = BitConverter.ToUInt16(il, position);
        position += 2;
        locals[index] = stack.Count > 0 ? stack.Pop() : IlStackValue.Unknown;
      }
      else if (op.Value == OpCodes.Ldloc_0.Value)
      {
        stack.Push(locals.TryGetValue(0, out var local0) ? local0 : IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Ldloc_1.Value)
      {
        stack.Push(locals.TryGetValue(1, out var local1) ? local1 : IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Ldloc_2.Value)
      {
        stack.Push(locals.TryGetValue(2, out var local2) ? local2 : IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Ldloc_3.Value)
      {
        stack.Push(locals.TryGetValue(3, out var local3) ? local3 : IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Ldloc_S.Value)
      {
        var index = il[position++];
        stack.Push(locals.TryGetValue(index, out var local) ? local : IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Ldloc.Value)
      {
        var index = BitConverter.ToUInt16(il, position);
        position += 2;
        stack.Push(locals.TryGetValue(index, out var local) ? local : IlStackValue.Unknown);
      }
      else if (op.Value == OpCodes.Ldfld.Value)
      {
        var token = BitConverter.ToInt32(il, position);
        position += 4;

        var owner = stack.Count > 0 ? stack.Pop() : IlStackValue.Unknown;
        FieldInfo? field;
        try
        {
          field = module.ResolveField(token);
        }
        catch
        {
          field = null;
        }

        if (field == null || owner.Path == null)
        {
          stack.Push(IlStackValue.Unknown);
          continue;
        }

        var path = owner.Path == "this" ? field.Name : $"{owner.Path}.{field.Name}";
        stack.Push(IlStackValue.Unknown.WithPath(path));
      }
      else if (op.Value == OpCodes.Ldsfld.Value)
      {
        var token = BitConverter.ToInt32(il, position);
        position += 4;

        FieldInfo? field;
        try
        {
          field = module.ResolveField(token);
        }
        catch
        {
          field = null;
        }

        if (field == null)
        {
          stack.Push(IlStackValue.Unknown);
          continue;
        }

        var typeName = field.DeclaringType?.Name ?? string.Empty;
        var path = string.IsNullOrWhiteSpace(typeName) ? field.Name : $"{typeName}.{field.Name}";
        stack.Push(IlStackValue.Unknown.WithPath(path));
      }
      else if (op.Value == OpCodes.Stfld.Value)
      {
        var token = BitConverter.ToInt32(il, position);
        position += 4;

        var value = stack.Count > 0 ? stack.Pop() : IlStackValue.Unknown;
        var owner = stack.Count > 0 ? stack.Pop() : IlStackValue.Unknown;

        FieldInfo? field;
        try
        {
          field = module.ResolveField(token);
        }
        catch
        {
          field = null;
        }

        if (field == null)
          continue;

        if (owner.HasPath("this"))
        {
          if (field.Name == "walkSpeed")
          {
            if (value.FloatValue.HasValue)
              walkSpeed = value.FloatValue.Value;
            else if (value.IntValue.HasValue)
              walkSpeed = value.IntValue.Value;
          }
          else if (field.Name == "baseDamage" && value.IntValue.HasValue)
          {
            baseDamage = value.IntValue.Value;
          }
          else if (field.Name == "minCoinDrop" && value.IntValue.HasValue)
          {
            minCoinDrop = value.IntValue.Value;
          }
          else if (field.Name == "maxCoinDrop" && value.IntValue.HasValue)
          {
            maxCoinDrop = value.IntValue.Value;
          }
          else if (field.Name == "canSpawnInSunlight" && value.IntValue.HasValue)
          {
            canSpawnInSunlight = value.IntValue.Value != 0;
          }
          else if (field.Name == "loot" && !string.IsNullOrWhiteSpace(value.Path))
          {
            loot = value.Path.Contains('.') ? value.Path[(value.Path.LastIndexOf('.') + 1)..] : value.Path;
          }
        }
        else if (
          owner.HasPath("health")
          || owner.HasPath("this.health")
          || owner.Path?.EndsWith(".health", StringComparison.Ordinal) == true
        )
        {
          if (field.Name == "maxHealth" && value.IntValue.HasValue)
            health = value.IntValue.Value;
          else if (field.Name == "defence" && value.IntValue.HasValue)
            defence = value.IntValue.Value;
        }
        else if (
          (owner.HasPath("phys") || owner.Path?.EndsWith(".phys", StringComparison.Ordinal) == true)
          && field.Name == "gravity"
          && value.HasPath("Vector3.Zero")
        )
        {
          flying = true;
        }
      }
      else
      {
        SkipOperand(il, ref position, op);
      }
    }
  }

  private static OpCode ReadOpCode(byte[] il, ref int position)
  {
    var first = il[position++];
    if (first != 0xFE)
      return SingleByteOpCodes[first];

    var second = il[position++];
    return MultiByteOpCodes[second];
  }

  private static void SkipOperand(byte[] il, ref int position, OpCode op)
  {
    switch (op.OperandType)
    {
      case OperandType.InlineNone:
        return;
      case OperandType.ShortInlineI:
      case OperandType.ShortInlineVar:
      case OperandType.ShortInlineBrTarget:
        position += 1;
        return;
      case OperandType.InlineVar:
        position += 2;
        return;
      case OperandType.InlineI:
      case OperandType.InlineBrTarget:
      case OperandType.InlineField:
      case OperandType.InlineMethod:
      case OperandType.InlineSig:
      case OperandType.InlineString:
      case OperandType.InlineTok:
      case OperandType.InlineType:
      case OperandType.ShortInlineR:
        position += 4;
        return;
      case OperandType.InlineI8:
      case OperandType.InlineR:
        position += 8;
        return;
      case OperandType.InlineSwitch:
      {
        var cases = BitConverter.ToInt32(il, position);
        position += 4 + (cases * 4);
        return;
      }
      default:
        return;
    }
  }

  private static readonly OpCode[] SingleByteOpCodes = BuildSingleByteOpCodeTable();
  private static readonly OpCode[] MultiByteOpCodes = BuildMultiByteOpCodeTable();

  private static OpCode[] BuildSingleByteOpCodeTable()
  {
    var table = new OpCode[0x100];
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
      if (field.GetValue(null) is not OpCode op)
        continue;

      var value = op.Value;
      if ((value & 0xFF00) != 0)
        continue;

      table[value & 0xFF] = op;
    }

    return table;
  }

  private static OpCode[] BuildMultiByteOpCodeTable()
  {
    var table = new OpCode[0x100];
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
      if (field.GetValue(null) is not OpCode op)
        continue;

      var value = op.Value;
      if ((value & 0xFF00) != 0xFE00)
        continue;

      table[value & 0xFF] = op;
    }

    return table;
  }
}

internal static class EntityParser
{
  public static Dictionary<Type, EntityEntry> entries = [];

  public static Dictionary<Type, EntityEntry> Parse()
  {
    Entity.RegisterEntities();

    foreach (var (entity, _) in Entity.entityToID)
      entries[entity] = new EntityEntry(entity);

    return entries;
  }
}
