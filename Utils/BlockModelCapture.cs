using System.Reflection;
using System.Reflection.Emit;
using Allumeria.Blocks.BlockModels;
using OpenTK.Mathematics;

internal sealed class BlockModelCapture
{
  internal sealed class ModelData
  {
    public List<CuboidMeshData> Cuboids { get; } = [];
    public HashSet<int> CuboidQuadIndices { get; } = [];
  }

  private readonly Dictionary<BlockModelQuads, ModelData> models = [];
  private readonly Dictionary<string, BlockModelQuads> fields = [];

  public static Dictionary<string, ModelData> Read()
  {
    var type = typeof(BlockModelQuads);
    var initializer = type.TypeInitializer ?? throw new InvalidOperationException("Block models have no initializer.");
    var body = initializer.GetMethodBody() ?? throw new InvalidOperationException("Block model initializer has no IL.");
    if (body.ExceptionHandlingClauses.Count != 0)
      throw new NotSupportedException("Block model initializer exception handlers cannot be replayed.");

    var replay = new DynamicMethod("CaptureBlockModels", typeof(void), [typeof(BlockModelCapture)], type.Module, true);
    var il = replay.GetILGenerator();
    foreach (var local in body.LocalVariables)
      il.DeclareLocal(local.LocalType, local.IsPinned);

    // Keep initializer writes local, including matrices and scalars read by later model definitions.
    var staticFields = type.GetFields(
      BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
    );
    var locals = staticFields.ToDictionary(field => field, field => il.DeclareLocal(field.FieldType));
    var instructions = IlReader.Read(initializer);
    var labels = instructions.ToDictionary(instruction => instruction.Offset, _ => il.DefineLabel());
    var captureCuboid = typeof(BlockModelCapture).GetMethod(
      nameof(AddCuboid),
      BindingFlags.Static | BindingFlags.NonPublic
    )!;
    var captureField = typeof(BlockModelCapture).GetMethod(
      nameof(StoreModel),
      BindingFlags.Static | BindingFlags.NonPublic
    )!;

    foreach (var instruction in instructions)
    {
      il.MarkLabel(labels[instruction.Offset]);
      var op = instruction.OpCode;
      var operand = instruction.Operand;

      if (operand is FieldInfo field && locals.TryGetValue(field, out var local))
      {
        if (op == OpCodes.Stsfld)
        {
          if (typeof(BlockModelQuads).IsAssignableFrom(field.FieldType))
          {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, field.Name);
            il.Emit(OpCodes.Call, captureField);
          }
          il.Emit(OpCodes.Stloc, local);
          continue;
        }
        if (op == OpCodes.Ldsfld || op == OpCodes.Ldsflda)
        {
          il.Emit(op == OpCodes.Ldsfld ? OpCodes.Ldloc : OpCodes.Ldloca, local);
          continue;
        }
      }

      if (op == OpCodes.Stsfld || op == OpCodes.Ldsflda)
        throw new NotSupportedException($"Cannot replay external static field access: {operand}.");

      if (
        (op == OpCodes.Call || op == OpCodes.Callvirt)
        && operand is MethodInfo method
        && method.DeclaringType == type
        && method.Name is nameof(BlockModelQuads.AddCuboid) or nameof(BlockModelQuads.AddInvertedCuboid)
      )
      {
        if (
          !method
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .SequenceEqual(new[] { typeof(Cuboid), typeof(Matrix4), typeof(int), typeof(int), typeof(int) })
        )
          throw new NotSupportedException($"Unsupported cuboid builder signature: {method}.");

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(method.Name == nameof(BlockModelQuads.AddInvertedCuboid) ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, captureCuboid);
        continue;
      }

      Emit(il, instruction, labels);
    }

    var capture = new BlockModelCapture();
    replay.CreateDelegate<Action<BlockModelCapture>>()(capture);
    return capture.fields.ToDictionary(pair => pair.Key, pair => capture.GetModel(pair.Value));
  }

  private ModelData GetModel(BlockModelQuads model)
  {
    if (!models.TryGetValue(model, out var data))
      models.Add(model, data = new ModelData());
    return data;
  }

  private static void StoreModel(BlockModelQuads model, BlockModelCapture capture, string name) =>
    capture.fields[name] = model;

  private static BlockModelQuads AddCuboid(
    BlockModelQuads model,
    Cuboid cuboid,
    Matrix4 matrix,
    int boundsX,
    int boundsY,
    int boundsZ,
    BlockModelCapture capture,
    bool inverted
  )
  {
    var data = capture.GetModel(model);
    data.Cuboids.Add(new CuboidMeshData(cuboid, matrix, boundsX, boundsY, boundsZ, inverted));
    var start = model.quads.Count;
    if (inverted)
      model.AddInvertedCuboid(cuboid, matrix, boundsX, boundsY, boundsZ);
    else
      model.AddCuboid(cuboid, matrix, boundsX, boundsY, boundsZ);

    for (var index = start; index < model.quads.Count; index++)
      data.CuboidQuadIndices.Add(index);
    return model;
  }

  private static void Emit(ILGenerator il, IlReader.Instruction instruction, Dictionary<int, Label> labels)
  {
    var op = instruction.OpCode;
    var operand = instruction.Operand;
    switch (op.OperandType)
    {
      case OperandType.InlineNone:
        il.Emit(op);
        break;
      case OperandType.ShortInlineBrTarget:
      case OperandType.InlineBrTarget:
        // Added capture instructions can push a short branch out of range.
        var longOp = op.OperandType == OperandType.ShortInlineBrTarget ? LongBranch(op) : op;
        il.Emit(longOp, labels[(int)operand!]);
        break;
      case OperandType.InlineSwitch:
        il.Emit(op, ((int[])operand!).Select(offset => labels[offset]).ToArray());
        break;
      case OperandType.ShortInlineI:
        il.Emit(op, (sbyte)operand!);
        break;
      case OperandType.InlineI:
        il.Emit(op, (int)operand!);
        break;
      case OperandType.InlineI8:
        il.Emit(op, (long)operand!);
        break;
      case OperandType.ShortInlineR:
        il.Emit(op, (float)operand!);
        break;
      case OperandType.InlineR:
        il.Emit(op, (double)operand!);
        break;
      case OperandType.ShortInlineVar:
        il.Emit(op, (byte)operand!);
        break;
      case OperandType.InlineVar:
        il.Emit(op, checked((short)Convert.ToInt32(operand)));
        break;
      default:
        switch (operand)
        {
          case string value:
            il.Emit(op, value);
            break;
          case Type value:
            il.Emit(op, value);
            break;
          case FieldInfo value:
            il.Emit(op, value);
            break;
          case ConstructorInfo value:
            il.Emit(op, value);
            break;
          case MethodInfo value:
            il.Emit(op, value);
            break;
          default:
            throw new NotSupportedException($"Cannot replay model IL at {instruction.Offset}: {op} {operand}.");
        }
        break;
    }
  }

  private static OpCode LongBranch(OpCode op)
  {
    var field = typeof(OpCodes).GetField(
      op.Name![..^2].Replace('.', '_'),
      BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase
    );
    return field?.GetValue(null) is OpCode longOp
      ? longOp
      : throw new NotSupportedException($"Unsupported short branch: {op}.");
  }
}
