using System.Diagnostics.CodeAnalysis;
using System.Reflection;

internal static class Reflection
{
  public sealed record ModelTexturePair(string? model, string? texture);

  public static bool GetPrivate<T>(object target, string fieldName, [NotNullWhen(true)] out T? value)
  {
    var type = target.GetType();
    while (type != null)
    {
      var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
      if (field?.GetValue(target) is T typedValue)
      {
        value = typedValue;
        return true;
      }

      type = type.BaseType;
    }

    value = default;
    return false;
  }

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
      _ when value != null && GetPrivate<string>(value, "strID", out var strId) => strId,
      _ when value != null && TryGetPublicStrId(value, out var publicStrId) => publicStrId,
      _ => null,
    };

  private static bool TryGetPublicStrId(object target, [NotNullWhen(true)] out string? value)
  {
    var type = target.GetType();

    var field = type.GetField("strID", BindingFlags.Instance | BindingFlags.Public);
    if (field?.GetValue(target) is string fieldValue && !string.IsNullOrWhiteSpace(fieldValue))
    {
      value = fieldValue;
      return true;
    }

    var property = type.GetProperty("strID", BindingFlags.Instance | BindingFlags.Public);
    if (property?.GetValue(target) is string propertyValue && !string.IsNullOrWhiteSpace(propertyValue))
    {
      value = propertyValue;
      return true;
    }

    value = null;
    return false;
  }

  public static Dictionary<string, object?> GetFieldsAsDict(object obj, HashSet<string> skipFields)
  {
    var result = new Dictionary<string, object?>(StringComparer.Ordinal);

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

      if (field.GetValue(null) is T instance)
        map.TryAdd(instance, field.Name);
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
    while (type is not null && type != baseType)
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
    var stack = new Stack<IlValue>();
    var locals = new Dictionary<int, IlValue>();
    ModelTexturePair? lastPair = null;
    string? pendingModel = null;
    string? pendingTexture = null;

    foreach (var instruction in IlReader.Read(method))
    {
      var op = instruction.OpCode;
      var operand = instruction.Operand;

      if (op.Value == System.Reflection.Emit.OpCodes.Ldarg_0.Value)
      {
        stack.Push(IlValue.This);
        continue;
      }

      if (
        op.Value == System.Reflection.Emit.OpCodes.Ldarg_1.Value
        || op.Value == System.Reflection.Emit.OpCodes.Ldarg_2.Value
        || op.Value == System.Reflection.Emit.OpCodes.Ldarg_3.Value
      )
      {
        stack.Push(IlValue.Unknown);
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldarg_S.Value)
      {
        stack.Push(Convert.ToInt32(operand) == 0 ? IlValue.This : IlValue.Unknown);
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldarg.Value)
      {
        stack.Push(Convert.ToInt32(operand) == 0 ? IlValue.This : IlValue.Unknown);
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldstr.Value)
      {
        stack.Push(new IlValue(null, operand as string));
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Dup.Value)
      {
        if (stack.Count > 0)
          stack.Push(stack.Peek());
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Pop.Value)
      {
        if (stack.Count > 0)
          stack.Pop();
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Stloc_0.Value)
      {
        locals[0] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Stloc_1.Value)
      {
        locals[1] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Stloc_2.Value)
      {
        locals[2] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Stloc_3.Value)
      {
        locals[3] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Stloc_S.Value)
      {
        var index = Convert.ToInt32(operand);
        locals[index] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Stloc.Value)
      {
        var index = Convert.ToInt32(operand);
        locals[index] = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldloc_0.Value)
      {
        stack.Push(locals.TryGetValue(0, out var local0) ? local0 : IlValue.Unknown);
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldloc_1.Value)
      {
        stack.Push(locals.TryGetValue(1, out var local1) ? local1 : IlValue.Unknown);
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldloc_2.Value)
      {
        stack.Push(locals.TryGetValue(2, out var local2) ? local2 : IlValue.Unknown);
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldloc_3.Value)
      {
        stack.Push(locals.TryGetValue(3, out var local3) ? local3 : IlValue.Unknown);
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldloc_S.Value)
      {
        var index = Convert.ToInt32(operand);
        stack.Push(locals.TryGetValue(index, out var local) ? local : IlValue.Unknown);
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldloc.Value)
      {
        var index = Convert.ToInt32(operand);
        stack.Push(locals.TryGetValue(index, out var local) ? local : IlValue.Unknown);
        continue;
      }

      if (op.Value == System.Reflection.Emit.OpCodes.Ldfld.Value)
      {
        var owner = stack.Count > 0 ? stack.Pop() : IlValue.Unknown;
        if (operand is not FieldInfo field || owner.Path == null)
        {
          stack.Push(IlValue.Unknown);
          continue;
        }

        var path = owner.Path == "this" ? field.Name : $"{owner.Path}.{field.Name}";
        stack.Push(new IlValue(path, null));
        continue;
      }

      if (
        op.Value != System.Reflection.Emit.OpCodes.Call.Value
        && op.Value != System.Reflection.Emit.OpCodes.Callvirt.Value
      )
        continue;

      if (operand is not MethodBase calledMethod)
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
}
