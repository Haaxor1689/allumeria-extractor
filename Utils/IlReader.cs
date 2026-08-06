using System.Reflection;
using System.Reflection.Emit;

internal static class IlReader
{
  public sealed record Instruction(int Offset, OpCode OpCode, object? Operand);

  public static IReadOnlyList<Instruction> Read(MethodBase method)
  {
    var body = method.GetMethodBody();
    var ilBytes = body?.GetILAsByteArray();
    if (ilBytes is null || ilBytes.Length == 0)
      return [];

    var instructions = new List<Instruction>(ilBytes.Length / 2);
    var offset = 0;

    while (offset < ilBytes.Length)
    {
      var instructionOffset = offset;
      var opcode = ReadOpCode(ilBytes, ref offset);
      var operand = ReadOperand(method, opcode, ilBytes, ref offset);
      instructions.Add(new Instruction(instructionOffset, opcode, operand));
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

  private static object? ReadOperand(MethodBase method, OpCode opcode, byte[] ilBytes, ref int offset)
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
    var table = new OpCode[256];
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
      if (field.GetValue(null) is not OpCode op || op.Size != 1)
        continue;

      table[(byte)op.Value] = op;
    }

    return table;
  }

  private static OpCode[] BuildMultiByteOpCodes()
  {
    var table = new OpCode[256];
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
      if (field.GetValue(null) is not OpCode op || op.Size != 2)
        continue;

      if ((op.Value & 0xFF00) != 0xFE00)
        continue;

      table[op.Value & 0xFF] = op;
    }

    return table;
  }
}