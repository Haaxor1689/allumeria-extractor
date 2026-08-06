using System.Reflection;
using System.Reflection.Emit;

internal static class ReflectionHelpers
{
  public sealed record ModelTexturePair(string? model, string? texture);

  public static object? TryConvertToExportable(object? value) =>
    value switch
    {
      null => null,
      bool b => (object?)b,
      int i when i != 0 => i,
      long l when l != 0 => l,
      float f when f != 0f => f,
      double d when d != 0.0 => d,
      decimal decVal when decVal != 0m => (float)decVal,
      string s when !string.IsNullOrEmpty(s) => s,
      Enum e => e.ToString(),
      Type t => t.Name,
      // For game-data objects (e.g. Effect) that carry a string ID, export that ID.
      _ when TryGetStrID(value) is { } strId => strId,
      _ => null,
    };

  public static string? TryGetStrID(object value)
  {
    var field = value.GetType().GetField("strID", BindingFlags.Public | BindingFlags.Instance);
    return field?.GetValue(value) as string;
  }

  public static Dictionary<string, object?> GetFieldsAsDict(object obj, HashSet<string> skipFields)
  {
    var result = new Dictionary<string, object?>(StringComparer.Ordinal);

    if (obj == null)
      return result;

    var objType = obj.GetType();
    var fields = objType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    foreach (var field in fields)
    {
      if (skipFields.Contains(field.Name))
        continue;

      try
      {
        var value = field.GetValue(obj);

        if (value == null)
          continue;

        // Handle common types
        if (value is int intVal && intVal != 0)
        {
          result[field.Name] = intVal;
        }
        else if (value is float floatVal && floatVal != 0f)
        {
          result[field.Name] = floatVal;
        }
        else if (value is double doubleVal && doubleVal != 0d)
        {
          result[field.Name] = doubleVal;
        }
        else if (value is bool boolVal)
        {
          result[field.Name] = boolVal;
        }
        else if (value is string strVal && !string.IsNullOrWhiteSpace(strVal))
        {
          result[field.Name] = strVal;
        }
        else if (value is Enum enumVal)
        {
          result[field.Name] = enumVal.ToString();
        }
      }
      catch
      {
        // Skip fields that can't be read
      }
    }

    return result;
  }

  public static Dictionary<T, string> BuildStaticInstanceNameMap<T, TClass>()
    where T : class
    where TClass : class
  {
    var map = new Dictionary<T, string>();

    var type = typeof(TClass);
    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);

    foreach (var field in fields)
    {
      if (!typeof(T).IsAssignableFrom(field.FieldType))
        continue;

      if (field.GetValue(null) is T instance && !map.ContainsKey(instance))
        map[instance] = field.Name;
    }

    return map;
  }

  public static Dictionary<T, string> BuildStaticInstanceNameMap<T>()
    where T : class
  {
    return BuildStaticInstanceNameMap<T, T>();
  }

  public static void PopulateSubclassFields(
    Dictionary<string, object?> target,
    object obj,
    Type baseType,
    HashSet<string> excludedFields,
    Func<string, string>? normalizeFieldName = null,
    Func<FieldInfo, object, Dictionary<string, object?>, bool>? customCallback = null
  )
  {
    var concreteType = obj.GetType();
    if (concreteType == baseType)
      return;

    var type = concreteType;
    while (type != baseType && type is not null)
    {
      foreach (
        var field in type.GetFields(
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )
      )
      {
        if (customCallback != null && customCallback(field, obj, target))
          continue;

        if (excludedFields.Contains(field.Name))
          continue;

        var key = normalizeFieldName != null ? normalizeFieldName(field.Name) : field.Name;
        if (target.ContainsKey(key))
          continue;

        var rawValue = field.GetValue(obj);
        var exportable = TryConvertToExportable(rawValue);
        if (exportable is null)
          continue;

        target[key] = exportable;
      }
      type = type.BaseType;
    }
  }

  public static bool IsSubtype<T>(Type t)
    where T : class
  {
    var cursor = t;
    while (cursor != null)
    {
      if (cursor == typeof(T))
        return true;
      cursor = cursor.BaseType;
    }
    return false;
  }

  public static ModelTexturePair GetEntityModelTexture(Type entityType)
  {
    // Build inheritance chain from root to leaf and keep the last this.model SetModel pair.
    var chain = new List<Type>();
    var cursor = entityType;
    while (cursor != null && cursor != typeof(object))
    {
      chain.Add(cursor);
      cursor = cursor.BaseType;
    }
    chain.Reverse();

    ModelTexturePair? lastPair = null;

    foreach (var t in chain)
    {
      foreach (
        var ctor in t.GetConstructors(
          BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly
        )
      )
      {
        var pair = ScanMethodForModelTexture(ctor);
        if (pair != null)
          lastPair = pair;
      }

      foreach (
        var method in t.GetMethods(
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )
      )
      {
        var pair = ScanMethodForModelTexture(method);
        if (pair != null)
          lastPair = pair;
      }
    }

    return lastPair ?? new ModelTexturePair(null, null);
  }

  private readonly record struct IlValue(string? Path, string? StringLiteral)
  {
    public static IlValue Unknown => new(null, null);
    public static IlValue This => new("this", null);

    public bool IsThisModelReceiver =>
      string.Equals(Path, "model", StringComparison.Ordinal)
      || string.Equals(Path, "this.model", StringComparison.Ordinal)
      || (Path?.EndsWith(".model", StringComparison.Ordinal) == true);
  }

  private static ModelTexturePair? ScanMethodForModelTexture(MethodBase method)
  {
    var body = method.GetMethodBody();
    if (body == null)
      return null;

    var il = body.GetILAsByteArray();
    if (il == null)
      return null;

    var stack = new Stack<IlValue>();
    var locals = new Dictionary<int, IlValue>();
    var module = method.Module;
    ModelTexturePair? lastPair = null;
    string? pendingModel = null;
    string? pendingTexture = null;
    var position = 0;

    while (position < il.Length)
    {
      var op = ReadOpCode(il, ref position);

      if (op.Value == OpCodes.Ldarg_0.Value)
      {
        stack.Push(IlValue.This);
        continue;
      }

      if (op.Value == OpCodes.Ldarg_1.Value || op.Value == OpCodes.Ldarg_2.Value || op.Value == OpCodes.Ldarg_3.Value)
      {
        stack.Push(IlValue.Unknown);
        continue;
      }

      if (op.Value == OpCodes.Ldarg_S.Value)
      {
        var argIndex = il[position++];
        stack.Push(argIndex == 0 ? IlValue.This : IlValue.Unknown);
        continue;
      }

      if (op.Value == OpCodes.Ldarg.Value)
      {
        var argIndex = BitConverter.ToUInt16(il, position);
        position += 2;
        stack.Push(argIndex == 0 ? IlValue.This : IlValue.Unknown);
        continue;
      }

      if (op.Value == OpCodes.Ldstr.Value)
      {
        var strToken = BitConverter.ToInt32(il, position);
        position += 4;

        string? str;
        try
        {
          str = module.ResolveString(strToken);
        }
        catch
        {
          str = null;
        }

        stack.Push(new IlValue(null, str));
        continue;
      }

      if (op.Value == OpCodes.Dup.Value)
      {
        if (stack.Count > 0)
          stack.Push(stack.Peek());
        continue;
      }

      if (op.Value == OpCodes.Pop.Value)
      {
        if (stack.Count > 0)
          stack.Pop();
        continue;
      }

      if (op.Value == OpCodes.Stloc_0.Value)
      {
        locals[0] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == OpCodes.Stloc_1.Value)
      {
        locals[1] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == OpCodes.Stloc_2.Value)
      {
        locals[2] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == OpCodes.Stloc_3.Value)
      {
        locals[3] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == OpCodes.Stloc_S.Value)
      {
        var index = il[position++];
        locals[index] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == OpCodes.Stloc.Value)
      {
        var index = BitConverter.ToUInt16(il, position);
        position += 2;
        locals[index] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == OpCodes.Ldloc_0.Value)
      {
        stack.Push(locals.TryGetValue(0, out var local0) ? local0 : IlValue.Unknown);
        continue;
      }

      if (op.Value == OpCodes.Ldloc_1.Value)
      {
        stack.Push(locals.TryGetValue(1, out var local1) ? local1 : IlValue.Unknown);
        continue;
      }

      if (op.Value == OpCodes.Ldloc_2.Value)
      {
        stack.Push(locals.TryGetValue(2, out var local2) ? local2 : IlValue.Unknown);
        continue;
      }

      if (op.Value == OpCodes.Ldloc_3.Value)
      {
        stack.Push(locals.TryGetValue(3, out var local3) ? local3 : IlValue.Unknown);
        continue;
      }

      if (op.Value == OpCodes.Ldloc_S.Value)
      {
        var index = il[position++];
        stack.Push(locals.TryGetValue(index, out var local) ? local : IlValue.Unknown);
        continue;
      }

      if (op.Value == OpCodes.Ldloc.Value)
      {
        var index = BitConverter.ToUInt16(il, position);
        position += 2;
        stack.Push(locals.TryGetValue(index, out var local) ? local : IlValue.Unknown);
        continue;
      }

      if (op.Value == OpCodes.Ldfld.Value)
      {
        var fieldToken = BitConverter.ToInt32(il, position);
        position += 4;

        var owner = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        FieldInfo? field;
        try
        {
          field = module.ResolveField(fieldToken);
        }
        catch
        {
          field = null;
        }

        if (field == null || owner.Path == null)
        {
          stack.Push(IlValue.Unknown);
          continue;
        }

        var path = owner.Path == "this" ? field.Name : $"{owner.Path}.{field.Name}";
        stack.Push(new IlValue(path, null));
        continue;
      }

      if (op.Value != OpCodes.Call.Value && op.Value != OpCodes.Callvirt.Value)
      {
        SkipOperand(il, ref position, op);
        continue;
      }

      var methodToken = BitConverter.ToInt32(il, position);
      position += 4;

      MethodBase? calledMethod;
      try
      {
        calledMethod = module.ResolveMethod(methodToken);
      }
      catch
      {
        continue;
      }

      if (calledMethod == null)
        continue;

      var parameterCount = calledMethod.GetParameters().Length;
      var args = new IlValue[parameterCount];
      for (var p = parameterCount - 1; p >= 0; p--)
        args[p] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;

      var receiver = !calledMethod.IsStatic && stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
      var firstArg = parameterCount > 0 ? args[0] : IlValue.Unknown;

      if (calledMethod.Name == "GetModel" && !string.IsNullOrWhiteSpace(firstArg.StringLiteral))
      {
        pendingModel = firstArg.StringLiteral;
      }
      else if (calledMethod.Name == "GetTexture" && !string.IsNullOrWhiteSpace(firstArg.StringLiteral))
      {
        pendingTexture = firstArg.StringLiteral;
      }
      else if (calledMethod.Name == "SetModel")
      {
        // Only consider model assignments that target this.model.
        if (
          receiver.IsThisModelReceiver
          && !string.IsNullOrWhiteSpace(pendingModel)
          && !string.IsNullOrWhiteSpace(pendingTexture)
        )
        {
          lastPair = new ModelTexturePair(pendingModel, pendingTexture);
        }

        pendingModel = null;
        pendingTexture = null;
      }

      if (calledMethod is MethodInfo methodInfo && methodInfo.ReturnType != typeof(void))
        stack.Push(IlValue.Unknown);
    }

    return lastPair;
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
