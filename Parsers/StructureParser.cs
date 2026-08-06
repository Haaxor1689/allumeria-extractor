using System.Reflection;
using System.Reflection.Emit;
using Allumeria.Blocks.Blocks;
using Allumeria.Blocks.Structures;
using Allumeria.ChunkManagement;
using Allumeria.Items.LootTables;

internal class StructureEntry : Dictionary<string, object?>
{
  public StructureEntry(StructureBuilder structure)
  {
    var className = structure.GetType().Name;
    this["id"] = className[..^7];
    this["chests"] = ReadChestEntriesFromRunMarkerCommand(structure);
  }

  private static List<object> ReadChestEntriesFromRunMarkerCommand(StructureBuilder structure)
  {
    var runMarkerCommand = structure
      .GetType()
      .GetMethod(
        "RunMarkerCommand",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        [typeof(Marker), typeof(World), typeof(Random)],
        modifiers: null
      );

    if (runMarkerCommand is null || runMarkerCommand.DeclaringType == typeof(StructureBuilder))
      return [];

    var instructions = ReadIlInstructions(runMarkerCommand);
    if (instructions.Count == 0)
      return [];

    var chests = new List<object>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    for (var index = 0; index < instructions.Count; index++)
    {
      var instruction = instructions[index];

      if (!IsWorldPlaceChestCall(instruction.Operand))
        continue;

      var chestField = FindPreviousField(instructions, index, typeof(Block));
      var lootField = FindPreviousField(instructions, index, typeof(LootDescription));
      if (chestField is null || lootField is null)
        continue;

      var dedupeKey = string.Concat(chestField.Name, "|", lootField.Name);
      if (!seen.Add(dedupeKey))
        continue;

      var chestEntry = new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        ["chest"] = chestField.Name,
        ["loot"] = lootField.Name,
      };

      chests.Add(chestEntry);
    }

    return chests;
  }

  private static FieldInfo? FindPreviousField(
    IReadOnlyList<IlInstruction> instructions,
    int startIndex,
    Type declaringType
  )
  {
    var minIndex = Math.Max(0, startIndex - 80);
    for (var i = startIndex - 1; i >= minIndex; i--)
    {
      if (instructions[i].Operand is not FieldInfo field)
        continue;

      if (field.DeclaringType == declaringType)
        return field;
    }

    return null;
  }

  private static bool IsWorldPlaceChestCall(object? operand)
  {
    return operand is MethodInfo method
      && method.DeclaringType == typeof(World)
      && string.Equals(method.Name, "PlaceChest", StringComparison.Ordinal);
  }

  private static List<IlInstruction> ReadIlInstructions(MethodInfo method)
  {
    var methodBody = method.GetMethodBody();
    var ilBytes = methodBody?.GetILAsByteArray();
    if (ilBytes is null || ilBytes.Length == 0)
      return [];

    var instructions = new List<IlInstruction>(ilBytes.Length / 2);
    var offset = 0;

    while (offset < ilBytes.Length)
    {
      var instructionOffset = offset;
      var opcode = ReadOpCode(ilBytes, ref offset);
      var operand = ReadOperand(method, opcode, ilBytes, ref offset);
      instructions.Add(new IlInstruction(instructionOffset, opcode, operand));
    }

    return instructions;
  }

  private static OpCode ReadOpCode(byte[] ilBytes, ref int offset)
  {
    var first = ilBytes[offset++];
    if (first != 0xFE)
      return SingleByteOpCodes[first];

    var second = ilBytes[offset++];
    return MultiByteOpCodes[second];
  }

  private static object? ReadOperand(MethodInfo method, OpCode opcode, byte[] ilBytes, ref int offset)
  {
    switch (opcode.OperandType)
    {
      case OperandType.InlineNone:
        return null;
      case OperandType.ShortInlineBrTarget:
      {
        var delta = (sbyte)ilBytes[offset++];
        return offset + delta;
      }
      case OperandType.InlineBrTarget:
      {
        var delta = BitConverter.ToInt32(ilBytes, offset);
        offset += 4;
        return offset + delta;
      }
      case OperandType.ShortInlineI:
        return (sbyte)ilBytes[offset++];
      case OperandType.InlineI:
      {
        var value = BitConverter.ToInt32(ilBytes, offset);
        offset += 4;
        return value;
      }
      case OperandType.InlineI8:
      {
        var value = BitConverter.ToInt64(ilBytes, offset);
        offset += 8;
        return value;
      }
      case OperandType.ShortInlineR:
      {
        var value = BitConverter.ToSingle(ilBytes, offset);
        offset += 4;
        return value;
      }
      case OperandType.InlineR:
      {
        var value = BitConverter.ToDouble(ilBytes, offset);
        offset += 8;
        return value;
      }
      case OperandType.ShortInlineVar:
        return ilBytes[offset++];
      case OperandType.InlineVar:
      {
        var value = BitConverter.ToUInt16(ilBytes, offset);
        offset += 2;
        return value;
      }
      case OperandType.InlineSwitch:
      {
        var caseCount = BitConverter.ToInt32(ilBytes, offset);
        offset += 4;

        var branchBase = offset + caseCount * 4;
        var targets = new int[caseCount];

        for (var i = 0; i < caseCount; i++)
        {
          var delta = BitConverter.ToInt32(ilBytes, offset);
          offset += 4;
          targets[i] = branchBase + delta;
        }

        return targets;
      }
      case OperandType.InlineString:
      {
        var metadataToken = BitConverter.ToInt32(ilBytes, offset);
        offset += 4;
        return TryResolve(() => method.Module.ResolveString(metadataToken));
      }
      case OperandType.InlineField:
      {
        var metadataToken = BitConverter.ToInt32(ilBytes, offset);
        offset += 4;
        return TryResolve(() => method.Module.ResolveField(metadataToken));
      }
      case OperandType.InlineMethod:
      {
        var metadataToken = BitConverter.ToInt32(ilBytes, offset);
        offset += 4;
        return TryResolve(() => method.Module.ResolveMethod(metadataToken));
      }
      case OperandType.InlineType:
      {
        var metadataToken = BitConverter.ToInt32(ilBytes, offset);
        offset += 4;
        return TryResolve(() => method.Module.ResolveType(metadataToken));
      }
      case OperandType.InlineTok:
      {
        var metadataToken = BitConverter.ToInt32(ilBytes, offset);
        offset += 4;
        return TryResolve(() => method.Module.ResolveMember(metadataToken));
      }
      case OperandType.InlineSig:
      {
        var metadataToken = BitConverter.ToInt32(ilBytes, offset);
        offset += 4;
        return metadataToken;
      }
      default:
        return null;
    }
  }

  private static T? TryResolve<T>(Func<T?> resolver)
    where T : class
  {
    try
    {
      return resolver();
    }
    catch
    {
      return null;
    }
  }

  private static readonly OpCode[] SingleByteOpCodes = BuildSingleByteOpCodes();
  private static readonly OpCode[] MultiByteOpCodes = BuildMultiByteOpCodes();

  private static OpCode[] BuildSingleByteOpCodes()
  {
    var opCodes = new OpCode[256];
    foreach (var code in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
      if (code.GetValue(null) is not OpCode opCode || opCode.Size != 1)
        continue;

      opCodes[(byte)opCode.Value] = opCode;
    }

    return opCodes;
  }

  private static OpCode[] BuildMultiByteOpCodes()
  {
    var opCodes = new OpCode[256];
    foreach (var code in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
      if (code.GetValue(null) is not OpCode opCode || opCode.Size != 2)
        continue;

      if ((opCode.Value & 0xFF00) != 0xFE00)
        continue;

      opCodes[opCode.Value & 0xFF] = opCode;
    }

    return opCodes;
  }

  private sealed record IlInstruction(int Offset, OpCode OpCode, object? Operand);
}

internal static class StructureParser
{
  public static Dictionary<StructureBuilder, StructureEntry> entries = [];

  public static Dictionary<StructureBuilder, StructureEntry> Parse()
  {
    var structures = StructureBuilder.structureBuilders.Where(structure =>
      structure != null && structure.GetType() != typeof(StructureBuilder)
    );

    foreach (var structure in structures)
      entries[structure] = new StructureEntry(structure);

    return entries;
  }
}
