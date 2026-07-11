using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class BlockModelParser
{
  public static List<object> Parse(string sourceRoot)
  {
    var blockModelsRoot = Path.Combine(sourceRoot, "Blocks", "BlockModels");
    if (!Directory.Exists(blockModelsRoot))
      return [];

    var multiBlockModelModes = DetectMultiBlockModelModes(sourceRoot);

    var entriesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
    var numericSymbolsByFile = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

    foreach (
      var file in Directory
        .EnumerateFiles(blockModelsRoot, "*.cs", SearchOption.TopDirectoryOnly)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    )
    {
      var root = SyntaxParsingUtils.ParseCompilationUnit(file);
      var numericSymbols = BuildNumericSymbolMap(root);
      numericSymbolsByFile[file] = numericSymbols;
      var matrixSymbols = BuildMatrixSymbolMap(root, numericSymbols);

      foreach (var field in SyntaxParsingUtils.FindPublicStaticFields(root))
      {
        var declaredType = field.Declaration.Type.ToString();
        if (!IsBlockModelType(declaredType))
          continue;

        foreach (var variable in field.Declaration.Variables)
        {
          var id = variable.Identifier.Text;
          if (string.IsNullOrWhiteSpace(id))
            continue;

          var initializer = variable.Initializer?.Value;
          var ctor = initializer is null ? null : SyntaxParsingUtils.TryGetRootObjectCreation(initializer);
          var sourceType = ctor?.Type.ToString() ?? declaredType;
          var typeName = NormalizeTypeName(sourceType);

          var meshes = BuildMeshesFromInitializer(initializer, numericSymbols, matrixSymbols);
          if (meshes.Count == 0 && IsDefaultCubeType(typeName))
            meshes = BuildDefaultCubeMeshes(typeName);

          if (multiBlockModelModes.TryGetValue(id, out var multiBlockMode) && multiBlockMode != MultiBlockMode.None)
            meshes = MergeSecondBlockIntoMeshes(meshes, multiBlockMode);

          var entry = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = id };

          entry["meshes"] = meshes;

          entriesById[id] = entry;
        }
      }
    }

    return entriesById
      .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
      .Select(pair => (object)pair.Value)
      .ToList();
  }

  private static List<object> BuildMeshesFromInitializer(
    ExpressionSyntax? initializer,
    IDictionary<string, double> symbols,
    IReadOnlyDictionary<string, double[]> matrixSymbols
  )
  {
    if (initializer is null)
      return [];

    var invocations = SyntaxParsingUtils.FindInvocations(initializer).Reverse().ToArray();
    var parts = new List<Dictionary<string, object?>>(256);
    var order = 0;

    foreach (var invocation in invocations)
    {
      var name = SyntaxParsingUtils.GetInvocationName(invocation);
      if (name == "AddCuboid")
      {
        var cuboid = TryParseCuboidArgument(invocation, symbols, matrixSymbols);
        if (cuboid is null)
          continue;

        var cuboidPart = CreateCuboidPart(cuboid);
        cuboidPart["order"] = order++;
        parts.Add(cuboidPart);
      }
      else if (name == "AddQuad")
      {
        if (TryParseQuadInvocation(invocation, symbols, matrixSymbols, out var quad))
        {
          quad["order"] = order++;
          parts.Add(quad);
        }
      }
    }

    return BuildMeshesFromParts(RemoveMirroredQuadDuplicates(parts));
  }

  private static List<Dictionary<string, object?>> RemoveMirroredQuadDuplicates(
    IEnumerable<Dictionary<string, object?>> parts
  )
  {
    var result = new List<Dictionary<string, object?>>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var part in parts)
    {
      if (!IsQuadPart(part, out var vertices))
      {
        result.Add(part);
        continue;
      }

      var key = BuildMirroredQuadKey(part, vertices);
      if (!seen.Add(key))
        continue;

      result.Add(part);
    }

    return result;
  }

  private static bool IsQuadPart(IDictionary<string, object?> part, out double[][] vertices)
  {
    vertices = [];
    if (
      !part.TryGetValue("type", out var sourceObj)
      || !string.Equals(sourceObj as string, "quad", StringComparison.Ordinal)
    )
      return false;

    if (
      !part.TryGetValue("vertices", out var verticesObj)
      || verticesObj is not double[][] values
      || values.Length != 4
    )
      return false;

    vertices = values;
    return true;
  }

  private static string BuildMirroredQuadKey(IDictionary<string, object?> part, IReadOnlyList<double[]> vertices)
  {
    var forward = string.Join("|", vertices.Select(FormatVector));
    var reverse = string.Join("|", vertices.Reverse().Select(FormatVector));
    var geometryKey = string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse;

    var flag = ReadInt(part, "flag");
    var normal = ReadInt(part, "normal");
    var textureIndex = ReadInt(part, "textureIndex");
    var uvKey = string.Empty;
    if (part.TryGetValue("uvs", out var uvsObj) && uvsObj is double[] uvs)
      uvKey = string.Join(",", uvs.Select(Round));

    var matrixKey = string.Empty;
    if (part.TryGetValue("matrix", out var matrixObj) && matrixObj is double[][] matrix)
      matrixKey = string.Join("|", matrix.Select(row => string.Join(",", row.Select(Round))));

    return $"{flag}:{normal}:{textureIndex}:{uvKey}:{matrixKey}:{geometryKey}";
  }

  private static string FormatVector(double[] vector)
  {
    return $"{Round(vector[0])},{Round(vector[1])},{Round(vector[2])}";
  }

  private static List<object> BuildDefaultCubeMeshes(string typeName)
  {
    var cuboid = new CuboidDef
    {
      MinX = 0,
      MinY = 0,
      MinZ = 0,
      MaxX = 16,
      MaxY = 16,
      MaxZ = 16,
      UseAo = true,
      Flag = 0,
      IgnorePaint = false,
      NoNormals = false,
      TextureIndices = GetDefaultCubeTextureIndices(typeName),
    };

    var part = CreateCuboidPart(cuboid);
    part["order"] = 0;
    return BuildMeshesFromParts([part]);
  }

  private static int[] GetDefaultCubeTextureIndices(string typeName)
  {
    if (string.Equals(typeName, "TopBottom", StringComparison.OrdinalIgnoreCase))
    {
      // Cuboid face order is top, bottom, +X, -X, +Z, -Z.
      // BlockModelTopBottom uses textures [top, side, bottom].
      return [0, 2, 1, 1, 1, 1];
    }

    if (string.Equals(typeName, "SixSided", StringComparison.OrdinalIgnoreCase))
    {
      // Cuboid face order is top, bottom, +X, -X, +Z, -Z.
      // BlockModelSixSided maps textures as +Z, -Z, +X, -X, +Y, -Y.
      return [4, 5, 2, 3, 0, 1];
    }

    return [0, 0, 0, 0, 0, 0];
  }

  private static List<object> BuildMeshesFromParts(IEnumerable<Dictionary<string, object?>> parts)
  {
    return parts
      .GroupBy(part => ReadInt(part, "flag"))
      .OrderBy(group => group.Key)
      .Select(group =>
      {
        var mesh = group
          .OrderBy(part => ReadInt(part, "order"))
          .Select(part =>
          {
            part.Remove("order");
            part.Remove("flag");
            part.Remove("ignorePaint");
            return (object)part;
          })
          .ToList();

        return (object)mesh;
      })
      .ToList();
  }

  private static Dictionary<string, object?> CreateCuboidPart(CuboidDef cuboid)
  {
    var part = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
      ["type"] = "cuboid",
      ["flag"] = cuboid.Flag,
      ["min"] = new[] { Round(cuboid.MinX / 16d), Round(cuboid.MinY / 16d), Round(cuboid.MinZ / 16d) },
      ["max"] = new[] { Round(cuboid.MaxX / 16d), Round(cuboid.MaxY / 16d), Round(cuboid.MaxZ / 16d) },
    };

    var textureIndices = EnsureArraySize(cuboid.TextureIndices, 6);
    if (!AreAllZero(textureIndices))
      part["textureIndices"] = textureIndices;

    var uvOffsets = BuildCuboidUvOffsets(cuboid);
    if (!AreAllZero(uvOffsets))
      part["uvOffsets"] = uvOffsets;

    if (cuboid.Matrix is { Length: 16 })
      part["matrix"] = SerializeMatrix(cuboid.Matrix);

    return part;
  }

  private static int[][] BuildCuboidUvOffsets(CuboidDef cuboid)
  {
    var offsets = new int[6][];
    for (var i = 0; i < 6; i++)
    {
      var offset = GetOffset(cuboid, i);
      offsets[i] = [offset.X, offset.Y];
    }

    return offsets;
  }

  private static Dictionary<string, object?> CreateQuadPart(
    IReadOnlyList<double[]> vertices,
    FaceUvDef faceUv,
    int normal,
    int textureIndex,
    int flag,
    string source,
    double[]? matrix
  )
  {
    var part = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
      ["type"] = source,
      ["flag"] = flag,
      ["vertices"] = vertices.Select(v => new[] { Round(v[0]), Round(v[1]), Round(v[2]) }).ToArray(),
    };

    if (normal != 0)
      part["normal"] = normal;

    if (textureIndex != 0)
      part["textureIndex"] = textureIndex;

    if (!IsDefaultQuadUvs(faceUv))
    {
      part["uvs"] = new[]
      {
        Round(faceUv.UMin / 16d),
        Round(faceUv.VMin / 16d),
        Round(faceUv.UMax / 16d),
        Round(faceUv.VMax / 16d),
      };
    }

    if (matrix is { Length: 16 })
      part["matrix"] = SerializeMatrix(matrix);

    return part;
  }

  private static bool TryParseQuadInvocation(
    InvocationExpressionSyntax invocation,
    IDictionary<string, double> symbols,
    IReadOnlyDictionary<string, double[]> matrixSymbols,
    out Dictionary<string, object?> quad
  )
  {
    quad = new Dictionary<string, object?>();

    var argMap = BuildArgumentMap(invocation);

    if (!TryGetArgument(argMap, 0, "verts", out var vertsExpression))
      return false;

    if (!TryParseVector3Array(vertsExpression, symbols, out var vertices) || vertices.Count != 4)
      return false;

    if (!TryGetArgument(argMap, 1, "faceTexture", out var faceTextureExpression))
      return false;

    if (!TryParseFaceUv(faceTextureExpression, symbols, out var faceUv))
      return false;

    if (
      !TryGetArgument(argMap, 2, "normal", out var normalExpression)
      || !TryEvaluateInt(normalExpression, symbols, out var normal)
    )
      return false;

    var flag = ReadIntArgument(argMap, 4, "flag", symbols, 0);
    var textureIndex = ReadIntArgument(argMap, 5, "textureIndex", symbols, 0);
    var matrix = ReadMatrixArgument(invocation.ArgumentList, 3, "matrix", symbols, matrixSymbols);
    var normalized = vertices.Select(vector => new[] { vector[0] / 16d, vector[1] / 16d, vector[2] / 16d }).ToArray();

    quad = CreateQuadPart(normalized, faceUv, normal, textureIndex, flag, "quad", matrix);
    return true;
  }

  private static CuboidDef? TryParseCuboidArgument(
    InvocationExpressionSyntax invocation,
    IDictionary<string, double> symbols,
    IReadOnlyDictionary<string, double[]> matrixSymbols
  )
  {
    var invocationArgMap = BuildArgumentMap(invocation);
    if (!TryGetArgument(invocationArgMap, 0, "cuboid", out var cuboidExpression))
      return null;

    var cuboidCreation = Unwrap(cuboidExpression) as ObjectCreationExpressionSyntax;
    if (cuboidCreation is null || !cuboidCreation.Type.ToString().Contains("Cuboid", StringComparison.Ordinal))
      return null;

    var cuboid = ParseCuboidDefinition(cuboidCreation, symbols);
    if (cuboid is null)
      return null;

    cuboid.Matrix = ReadMatrixArgument(invocation.ArgumentList, 1, "matrix", symbols, matrixSymbols);
    return cuboid;
  }

  private static CuboidDef? ParseCuboidDefinition(
    ObjectCreationExpressionSyntax cuboidCreation,
    IDictionary<string, double> symbols
  )
  {
    var argMap = BuildArgumentMap(cuboidCreation.ArgumentList);

    if (!TryEvaluateInt(GetRequiredArgument(argMap, 0, symbols), symbols, out var minX))
      return null;
    if (!TryEvaluateInt(GetRequiredArgument(argMap, 1, symbols), symbols, out var minY))
      return null;
    if (!TryEvaluateInt(GetRequiredArgument(argMap, 2, symbols), symbols, out var minZ))
      return null;
    if (!TryEvaluateInt(GetRequiredArgument(argMap, 3, symbols), symbols, out var maxX))
      return null;
    if (!TryEvaluateInt(GetRequiredArgument(argMap, 4, symbols), symbols, out var maxY))
      return null;
    if (!TryEvaluateInt(GetRequiredArgument(argMap, 5, symbols), symbols, out var maxZ))
      return null;

    var useAo = ReadBoolArgument(argMap, 6, "useAO", symbols, true);
    var flag = ReadIntArgument(argMap, 8, "flag", symbols, 0);
    var ignorePaint = ReadBoolArgument(argMap, 9, "ignorePaint", symbols, false);
    var noNormals = ReadBoolArgument(argMap, 10, "noNormals", symbols, false);
    var textureIndices = ReadIntArrayArgument(argMap, 11, "textureIndices", symbols, 6) ?? [0, 0, 0, 0, 0, 0];
    var uvOffsets = ReadVector2ArrayArgument(argMap, 7, "uvOffsets", symbols);

    return new CuboidDef
    {
      MinX = minX,
      MinY = minY,
      MinZ = minZ,
      MaxX = maxX,
      MaxY = maxY,
      MaxZ = maxZ,
      UseAo = useAo,
      Flag = flag,
      IgnorePaint = ignorePaint,
      NoNormals = noNormals,
      TextureIndices = EnsureArraySize(textureIndices, 6),
      UvOffsets = uvOffsets,
    };
  }

  private static bool TryParseFaceUv(ExpressionSyntax expression, IDictionary<string, double> symbols, out FaceUvDef uv)
  {
    uv = new FaceUvDef(0, 0, 16, 16);
    var creation = Unwrap(expression) as ObjectCreationExpressionSyntax;
    if (creation is null || !creation.Type.ToString().Contains("FaceUV", StringComparison.Ordinal))
      return false;

    var args = creation.ArgumentList?.Arguments;
    if (args is null || args.Value.Count < 4)
      return false;

    if (!TryEvaluateInt(args.Value[0].Expression, symbols, out var umin))
      return false;
    if (!TryEvaluateInt(args.Value[1].Expression, symbols, out var vmin))
      return false;
    if (!TryEvaluateInt(args.Value[2].Expression, symbols, out var umax))
      return false;
    if (!TryEvaluateInt(args.Value[3].Expression, symbols, out var vmax))
      return false;

    uv = new FaceUvDef(umin, vmin, umax, vmax);
    return true;
  }

  private static Dictionary<string, double> BuildNumericSymbolMap(CompilationUnitSyntax root)
  {
    var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    foreach (var field in SyntaxParsingUtils.FindPublicStaticFields(root))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var initializer = variable.Initializer?.Value;
        if (initializer is null)
          continue;

        if (TryEvaluateDouble(initializer, map, out var value))
          map[variable.Identifier.Text] = value;
      }
    }

    return map;
  }

  private static IReadOnlyDictionary<string, double[]> BuildMatrixSymbolMap(
    CompilationUnitSyntax root,
    IDictionary<string, double> numericSymbols
  )
  {
    var map = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

    foreach (var field in SyntaxParsingUtils.FindPublicStaticFields(root))
    {
      var declaredType = field.Declaration.Type.ToString();
      if (!declaredType.Contains("Matrix4", StringComparison.Ordinal))
        continue;

      foreach (var variable in field.Declaration.Variables)
      {
        var initializer = variable.Initializer?.Value;
        if (initializer is null)
          continue;

        if (TryEvaluateMatrix(initializer, numericSymbols, map, out var matrix))
          map[variable.Identifier.Text] = matrix;
      }
    }

    return map;
  }

  private static bool TryParseVector3Array(
    ExpressionSyntax expression,
    IDictionary<string, double> symbols,
    out List<double[]> vectors
  )
  {
    vectors = [];
    var reduced = Unwrap(expression);

    InitializerExpressionSyntax? initializer = reduced switch
    {
      ArrayCreationExpressionSyntax arrayCreation => arrayCreation.Initializer,
      ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer,
      InitializerExpressionSyntax init => init,
      _ => null,
    };

    if (initializer is null)
      return false;

    foreach (var value in initializer.Expressions)
    {
      if (!TryParseVector3Expression(value, symbols, out var vector))
        return false;

      vectors.Add(vector);
    }

    return vectors.Count > 0;
  }

  private static bool TryParseVector3Expression(
    ExpressionSyntax expression,
    IDictionary<string, double> symbols,
    out double[] vector
  )
  {
    vector = [0, 0, 0];
    var reduced = Unwrap(expression);

    if (
      reduced is ObjectCreationExpressionSyntax creation
      && creation.Type.ToString().Contains("Vector3", StringComparison.Ordinal)
    )
    {
      var args = creation.ArgumentList?.Arguments;
      if (args is null || args.Value.Count < 3)
        return false;

      if (!TryEvaluateDouble(args.Value[0].Expression, symbols, out var x))
        return false;
      if (!TryEvaluateDouble(args.Value[1].Expression, symbols, out var y))
        return false;
      if (!TryEvaluateDouble(args.Value[2].Expression, symbols, out var z))
        return false;

      vector = [x, y, z];
      return true;
    }

    if (reduced is BinaryExpressionSyntax binary)
    {
      if (binary.Kind() == SyntaxKind.MultiplyExpression)
      {
        if (
          TryParseVector3Expression(binary.Left, symbols, out var leftVector)
          && TryEvaluateDouble(binary.Right, symbols, out var rightScalar)
        )
        {
          vector = [leftVector[0] * rightScalar, leftVector[1] * rightScalar, leftVector[2] * rightScalar];
          return true;
        }

        if (
          TryEvaluateDouble(binary.Left, symbols, out var leftScalar)
          && TryParseVector3Expression(binary.Right, symbols, out var rightVector)
        )
        {
          vector = [rightVector[0] * leftScalar, rightVector[1] * leftScalar, rightVector[2] * leftScalar];
          return true;
        }
      }

      if (binary.Kind() == SyntaxKind.AddExpression || binary.Kind() == SyntaxKind.SubtractExpression)
      {
        if (
          TryParseVector3Expression(binary.Left, symbols, out var left)
          && TryParseVector3Expression(binary.Right, symbols, out var right)
        )
        {
          var sign = binary.Kind() == SyntaxKind.SubtractExpression ? -1d : 1d;
          vector = [left[0] + sign * right[0], left[1] + sign * right[1], left[2] + sign * right[2]];
          return true;
        }
      }
    }

    return false;
  }

  private static bool TryEvaluateInt(ExpressionSyntax expression, IDictionary<string, double> symbols, out int value)
  {
    value = 0;
    if (!TryEvaluateDouble(expression, symbols, out var numeric))
      return false;

    value = (int)Math.Round(numeric, MidpointRounding.AwayFromZero);
    return true;
  }

  private static bool TryEvaluateDouble(
    ExpressionSyntax expression,
    IDictionary<string, double> symbols,
    out double value
  )
  {
    value = 0;
    var reduced = Unwrap(expression);

    if (reduced is LiteralExpressionSyntax literal)
    {
      if (literal.Token.Value is int intValue)
      {
        value = intValue;
        return true;
      }

      if (literal.Token.Value is long longValue)
      {
        value = longValue;
        return true;
      }

      if (literal.Token.Value is float floatValue)
      {
        value = floatValue;
        return true;
      }

      if (literal.Token.Value is double doubleValue)
      {
        value = doubleValue;
        return true;
      }

      if (literal.Token.Value is decimal decimalValue)
      {
        value = (double)decimalValue;
        return true;
      }
    }

    if (reduced is PrefixUnaryExpressionSyntax prefix)
    {
      if (
        prefix.Kind() == SyntaxKind.UnaryMinusExpression
        && TryEvaluateDouble(prefix.Operand, symbols, out var negated)
      )
      {
        value = -negated;
        return true;
      }

      if (
        prefix.Kind() == SyntaxKind.UnaryPlusExpression
        && TryEvaluateDouble(prefix.Operand, symbols, out var positive)
      )
      {
        value = positive;
        return true;
      }
    }

    if (reduced is BinaryExpressionSyntax binary)
    {
      if (
        !TryEvaluateDouble(binary.Left, symbols, out var left)
        || !TryEvaluateDouble(binary.Right, symbols, out var right)
      )
        return false;

      if (binary.Kind() == SyntaxKind.AddExpression)
      {
        value = left + right;
        return true;
      }

      if (binary.Kind() == SyntaxKind.SubtractExpression)
      {
        value = left - right;
        return true;
      }

      if (binary.Kind() == SyntaxKind.MultiplyExpression)
      {
        value = left * right;
        return true;
      }

      if (binary.Kind() == SyntaxKind.DivideExpression && Math.Abs(right) > double.Epsilon)
      {
        value = left / right;
        return true;
      }

      return false;
    }

    if (
      reduced is IdentifierNameSyntax identifier
      && symbols.TryGetValue(identifier.Identifier.Text, out var symbolValue)
    )
    {
      value = symbolValue;
      return true;
    }

    if (reduced is MemberAccessExpressionSyntax memberAccess)
    {
      var member = memberAccess.Name.Identifier.Text;
      if (symbols.TryGetValue(member, out var memberValue))
      {
        value = memberValue;
        return true;
      }
    }

    return false;
  }

  private static bool ReadBoolArgument(
    IReadOnlyDictionary<string, ExpressionSyntax> argMap,
    int position,
    string name,
    IDictionary<string, double> symbols,
    bool fallback
  )
  {
    if (!TryGetArgument(argMap, position, name, out var expression))
      return fallback;

    var reduced = Unwrap(expression);
    if (reduced.Kind() == SyntaxKind.TrueLiteralExpression)
      return true;
    if (reduced.Kind() == SyntaxKind.FalseLiteralExpression)
      return false;

    if (TryEvaluateDouble(reduced, symbols, out var numeric))
      return Math.Abs(numeric) > double.Epsilon;

    return fallback;
  }

  private static int ReadIntArgument(
    IReadOnlyDictionary<string, ExpressionSyntax> argMap,
    int position,
    string name,
    IDictionary<string, double> symbols,
    int fallback
  )
  {
    if (!TryGetArgument(argMap, position, name, out var expression))
      return fallback;

    return TryEvaluateInt(expression, symbols, out var parsed) ? parsed : fallback;
  }

  private static int[]? ReadIntArrayArgument(
    IReadOnlyDictionary<string, ExpressionSyntax> argMap,
    int position,
    string name,
    IDictionary<string, double> symbols,
    int expectedLength
  )
  {
    if (!TryGetArgument(argMap, position, name, out var expression))
      return null;

    var reduced = Unwrap(expression);
    InitializerExpressionSyntax? initializer = reduced switch
    {
      ArrayCreationExpressionSyntax arrayCreation => arrayCreation.Initializer,
      ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer,
      _ => null,
    };

    if (initializer is null)
      return new int[expectedLength];

    var values = new List<int>(expectedLength);
    foreach (var element in initializer.Expressions)
    {
      if (TryEvaluateInt(element, symbols, out var intValue))
        values.Add(intValue);
    }

    return EnsureArraySize(values.ToArray(), expectedLength);
  }

  private static (int X, int Y)[]? ReadVector2ArrayArgument(
    IReadOnlyDictionary<string, ExpressionSyntax> argMap,
    int position,
    string name,
    IDictionary<string, double> symbols
  )
  {
    if (!TryGetArgument(argMap, position, name, out var expression))
      return null;

    var reduced = Unwrap(expression);
    InitializerExpressionSyntax? initializer = reduced switch
    {
      ArrayCreationExpressionSyntax arrayCreation => arrayCreation.Initializer,
      ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer,
      _ => null,
    };

    if (initializer is null)
      return null;

    var list = new List<(int X, int Y)>();
    foreach (var value in initializer.Expressions)
    {
      var creation = Unwrap(value) as ObjectCreationExpressionSyntax;
      if (creation is null || !creation.Type.ToString().Contains("Vector2i", StringComparison.Ordinal))
        continue;

      var args = creation.ArgumentList?.Arguments;
      if (args is null || args.Value.Count < 2)
        continue;

      if (!TryEvaluateInt(args.Value[0].Expression, symbols, out var x))
        continue;
      if (!TryEvaluateInt(args.Value[1].Expression, symbols, out var y))
        continue;

      list.Add((x, y));
    }

    return list.Count == 0 ? null : list.ToArray();
  }

  private static ExpressionSyntax GetRequiredArgument(
    IReadOnlyDictionary<string, ExpressionSyntax> argMap,
    int position,
    IDictionary<string, double> symbols
  )
  {
    if (TryGetArgument(argMap, position, string.Empty, out var expression))
      return expression;

    return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0));
  }

  private static IReadOnlyDictionary<string, ExpressionSyntax> BuildArgumentMap(InvocationExpressionSyntax invocation)
  {
    return BuildArgumentMap(invocation.ArgumentList);
  }

  private static IReadOnlyDictionary<string, ExpressionSyntax> BuildArgumentMap(ArgumentListSyntax? argumentList)
  {
    var map = new Dictionary<string, ExpressionSyntax>(StringComparer.OrdinalIgnoreCase);
    if (argumentList is null)
      return map;

    for (var i = 0; i < argumentList.Arguments.Count; i++)
    {
      var argument = argumentList.Arguments[i];
      map[$"@{i}"] = argument.Expression;

      if (argument.NameColon is not null)
        map[argument.NameColon.Name.Identifier.Text] = argument.Expression;
    }

    return map;
  }

  private static bool TryGetArgument(
    IReadOnlyDictionary<string, ExpressionSyntax> argMap,
    int position,
    string name,
    out ExpressionSyntax expression
  )
  {
    if (!string.IsNullOrWhiteSpace(name) && argMap.TryGetValue(name, out expression!))
      return true;

    return argMap.TryGetValue($"@{position}", out expression!);
  }

  private static int ReadInt(IDictionary<string, object?> map, string key)
  {
    return map.TryGetValue(key, out var value) && value is int intValue ? intValue : 0;
  }

  private static double[]? ReadMatrixArgument(
    ArgumentListSyntax? argumentList,
    int position,
    string name,
    IDictionary<string, double> numericSymbols,
    IReadOnlyDictionary<string, double[]> matrixSymbols
  )
  {
    if (!TryGetOptionalArgument(argumentList, position, name, out var expression))
      return null;

    var reduced = Unwrap(expression);
    if (IsDefaultMatrixExpression(reduced))
      return null;

    return TryEvaluateMatrix(reduced, numericSymbols, matrixSymbols, out var matrix) ? matrix : null;
  }

  private static bool IsDefaultMatrixExpression(ExpressionSyntax expression)
  {
    if (expression is DefaultExpressionSyntax defaultExpression)
      return defaultExpression.Type.ToString().Contains("Matrix4", StringComparison.Ordinal);

    if (
      expression is ObjectCreationExpressionSyntax creation
      && creation.Type.ToString().Contains("Matrix4", StringComparison.Ordinal)
      && (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0)
    )
    {
      return true;
    }

    if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.DefaultLiteralExpression))
      return true;

    return false;
  }

  private static bool TryEvaluateMatrix(
    ExpressionSyntax expression,
    IDictionary<string, double> numericSymbols,
    IReadOnlyDictionary<string, double[]> matrixSymbols,
    out double[] matrix
  )
  {
    matrix = ZeroMatrix();

    var reduced = Unwrap(expression);
    if (IsDefaultMatrixExpression(reduced))
      return false;

    if (
      reduced is IdentifierNameSyntax identifier
      && matrixSymbols.TryGetValue(identifier.Identifier.Text, out var named)
    )
    {
      matrix = (double[])named.Clone();
      return true;
    }

    if (
      reduced is MemberAccessExpressionSyntax member
      && matrixSymbols.TryGetValue(member.Name.Identifier.Text, out var memberMatrix)
    )
    {
      matrix = (double[])memberMatrix.Clone();
      return true;
    }

    if (reduced is BinaryExpressionSyntax binary && binary.Kind() == SyntaxKind.MultiplyExpression)
    {
      if (
        TryEvaluateMatrix(binary.Left, numericSymbols, matrixSymbols, out var left)
        && TryEvaluateMatrix(binary.Right, numericSymbols, matrixSymbols, out var right)
      )
      {
        matrix = MultiplyMatrices(left, right);
        return true;
      }

      return false;
    }

    if (reduced is not InvocationExpressionSyntax invocation)
      return false;

    if (invocation.Expression is not MemberAccessExpressionSyntax invocationMember)
      return false;

    if (!invocationMember.Expression.ToString().EndsWith("Matrix4", StringComparison.Ordinal))
      return false;

    var name = invocationMember.Name.Identifier.Text;
    var args = invocation.ArgumentList.Arguments;

    switch (name)
    {
      case "CreateTranslation":
        if (args.Count == 1 && TryParseVector3Expression(args[0].Expression, numericSymbols, out var translation))
        {
          matrix = CreateTranslationMatrix(translation[0], translation[1], translation[2]);
          return true;
        }
        break;

      case "CreateRotationX":
        if (args.Count >= 1 && TryEvaluateDouble(args[0].Expression, numericSymbols, out var rx))
        {
          matrix = CreateRotationXMatrix(rx);
          return true;
        }
        break;

      case "CreateRotationY":
        if (args.Count >= 1 && TryEvaluateDouble(args[0].Expression, numericSymbols, out var ry))
        {
          matrix = CreateRotationYMatrix(ry);
          return true;
        }
        break;

      case "CreateRotationZ":
        if (args.Count >= 1 && TryEvaluateDouble(args[0].Expression, numericSymbols, out var rz))
        {
          matrix = CreateRotationZMatrix(rz);
          return true;
        }
        break;
    }

    return false;
  }

  private static bool TryGetOptionalArgument(
    ArgumentListSyntax? argumentList,
    int position,
    string name,
    out ExpressionSyntax expression
  )
  {
    expression = null!;
    if (argumentList is null)
      return false;

    foreach (var argument in argumentList.Arguments)
    {
      if (argument.NameColon is null)
        continue;

      if (!string.Equals(argument.NameColon.Name.Identifier.Text, name, StringComparison.OrdinalIgnoreCase))
        continue;

      expression = argument.Expression;
      return true;
    }

    if (argumentList.Arguments.Count <= position)
      return false;

    var positional = argumentList.Arguments[position];
    if (positional.NameColon is not null)
      return false;

    expression = positional.Expression;
    return true;
  }

  private static double[][] SerializeMatrix(IReadOnlyList<double> matrix)
  {
    return
    [
      [Round(matrix[0]), Round(matrix[1]), Round(matrix[2]), Round(matrix[3])],
      [Round(matrix[4]), Round(matrix[5]), Round(matrix[6]), Round(matrix[7])],
      [Round(matrix[8]), Round(matrix[9]), Round(matrix[10]), Round(matrix[11])],
      [Round(matrix[12]), Round(matrix[13]), Round(matrix[14]), Round(matrix[15])],
    ];
  }

  private static double[] ZeroMatrix()
  {
    return new double[16];
  }

  private static double[] IdentityMatrix()
  {
    return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  }

  private static double[] CreateTranslationMatrix(double x, double y, double z)
  {
    var matrix = IdentityMatrix();
    matrix[12] = x;
    matrix[13] = y;
    matrix[14] = z;
    return matrix;
  }

  private static double[] CreateRotationXMatrix(double radians)
  {
    var cos = Math.Cos(radians);
    var sin = Math.Sin(radians);
    return [1, 0, 0, 0, 0, cos, sin, 0, 0, -sin, cos, 0, 0, 0, 0, 1];
  }

  private static double[] CreateRotationYMatrix(double radians)
  {
    var cos = Math.Cos(radians);
    var sin = Math.Sin(radians);
    return [cos, 0, -sin, 0, 0, 1, 0, 0, sin, 0, cos, 0, 0, 0, 0, 1];
  }

  private static double[] CreateRotationZMatrix(double radians)
  {
    var cos = Math.Cos(radians);
    var sin = Math.Sin(radians);
    return [cos, sin, 0, 0, -sin, cos, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  }

  private static double[] MultiplyMatrices(IReadOnlyList<double> left, IReadOnlyList<double> right)
  {
    var result = new double[16];

    for (var row = 0; row < 4; row++)
    {
      for (var column = 0; column < 4; column++)
      {
        var sum = 0d;
        for (var k = 0; k < 4; k++)
          sum += left[row * 4 + k] * right[k * 4 + column];

        result[row * 4 + column] = sum;
      }
    }

    return result;
  }

  private static (int X, int Y) GetOffset(CuboidDef cuboid, int index)
  {
    if (cuboid.UvOffsets is null || index < 0 || index >= cuboid.UvOffsets.Length)
      return (0, 0);

    return cuboid.UvOffsets[index];
  }

  private static int[] EnsureArraySize(int[] values, int length)
  {
    if (values.Length == length)
      return values;

    var resized = new int[length];
    Array.Copy(values, resized, Math.Min(values.Length, length));
    return resized;
  }

  private static bool AreAllZero(IEnumerable<int> values)
  {
    return values.All(v => v == 0);
  }

  private static bool AreAllZero(IEnumerable<int[]> values)
  {
    return values.All(pair => pair.Length == 0 || pair.All(v => v == 0));
  }

  private static bool IsDefaultQuadUvs(FaceUvDef uv)
  {
    return uv.UMin == 0 && uv.VMin == 0 && uv.UMax == 16 && uv.VMax == 16;
  }

  private static double Round(double value)
  {
    return Math.Round(value, 6, MidpointRounding.AwayFromZero);
  }

  private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
  {
    ExpressionSyntax current = expression;
    while (true)
    {
      switch (current)
      {
        case ParenthesizedExpressionSyntax parenthesized:
          current = parenthesized.Expression;
          continue;
        case CastExpressionSyntax cast:
          current = cast.Expression;
          continue;
        default:
          return current;
      }
    }
  }

  private static bool IsDefaultCubeType(string typeName)
  {
    return string.Equals(typeName, "TopBottom", StringComparison.OrdinalIgnoreCase)
      || string.Equals(typeName, "SixSided", StringComparison.OrdinalIgnoreCase)
      || string.Equals(typeName, "", StringComparison.Ordinal);
  }

  private static Dictionary<string, MultiBlockMode> DetectMultiBlockModelModes(string sourceRoot)
  {
    var result = new Dictionary<string, MultiBlockMode>(StringComparer.OrdinalIgnoreCase);
    var blockClassesRoot = Path.Combine(sourceRoot, "Blocks", "Blocks");
    var blockRootFile = Path.Combine(blockClassesRoot, "Block.cs");

    if (!File.Exists(blockRootFile))
      return result;

    var blockRoot = SyntaxParsingUtils.ParseCompilationUnit(blockRootFile);
    var classificationCache = new Dictionary<string, MultiBlockMode>(StringComparer.Ordinal);

    foreach (var field in SyntaxParsingUtils.FindPublicStaticFields(blockRoot))
    {
      foreach (var variable in field.Declaration.Variables)
      {
        var initializer = variable.Initializer?.Value;
        if (initializer is null)
          continue;

        var blockTypeName = NormalizeTypeName(
          SyntaxParsingUtils.TryGetRootObjectCreation(initializer)?.Type.ToString() ?? field.Declaration.Type.ToString()
        );
        var multiBlockMode = DetectBlockMultiBlockMode(blockClassesRoot, blockTypeName, classificationCache);

        if (multiBlockMode == MultiBlockMode.None)
          continue;

        foreach (var invocation in SyntaxParsingUtils.FindInvocations(initializer))
        {
          if (
            !string.Equals(SyntaxParsingUtils.GetInvocationName(invocation), "SetBlockModel", StringComparison.Ordinal)
          )
            continue;

          if (TryReadModelIdFromSetBlockModelInvocation(invocation, out var modelId))
            result[modelId] = multiBlockMode;
        }
      }
    }

    return result;
  }

  private static MultiBlockMode DetectBlockMultiBlockMode(
    string blockClassesRoot,
    string blockTypeName,
    IDictionary<string, MultiBlockMode> cache
  )
  {
    if (cache.TryGetValue(blockTypeName, out var cached))
      return cached;

    var blockTypeFile = Path.Combine(blockClassesRoot, $"{blockTypeName}.cs");
    if (!File.Exists(blockTypeFile))
      return cache[blockTypeName] = MultiBlockMode.None;

    var root = SyntaxParsingUtils.ParseCompilationUnit(blockTypeFile);

    var getModelFlag = root.DescendantNodes()
      .OfType<MethodDeclarationSyntax>()
      .FirstOrDefault(method =>
        string.Equals(method.Identifier.Text, "GetModelFlag", StringComparison.Ordinal)
        && method.ParameterList.Parameters.Count == 1
      );

    if (getModelFlag is null)
      return cache[blockTypeName] = MultiBlockMode.None;

    var getModelFlagText = getModelFlag.ToString();
    var isFrontBack =
      getModelFlagText.Contains("state.front", StringComparison.Ordinal)
      || getModelFlagText.Contains("state.back", StringComparison.Ordinal)
      || getModelFlagText.Contains("front ?", StringComparison.Ordinal)
      || getModelFlagText.Contains("back ?", StringComparison.Ordinal);

    if (isFrontBack)
      return cache[blockTypeName] = MultiBlockMode.FrontBack;

    var isTopBottom =
      root.ToString().Contains("state.top", StringComparison.Ordinal)
      || root.ToString().Contains("state.bottom", StringComparison.Ordinal);

    if (isTopBottom)
      return cache[blockTypeName] = MultiBlockMode.TopBottom;

    return cache[blockTypeName] = MultiBlockMode.None;
  }

  private static List<object> MergeSecondBlockIntoMeshes(List<object> meshes, MultiBlockMode mode)
  {
    if (mode == MultiBlockMode.TopBottom)
    {
      return meshes
        .Select(meshObj =>
        {
          var faces = AsFaceList(meshObj);
          var merged = new List<object>(faces.Count * 2);
          merged.AddRange(CloneFacesWithTranslation(faces, 0, 0, 0));
          merged.AddRange(CloneFacesWithTranslation(faces, 0, 1, 0));
          return (object)merged;
        })
        .ToList();
    }

    if (mode == MultiBlockMode.FrontBack && meshes.Count >= 2)
    {
      var firstFaces = AsFaceList(meshes[0]);
      var secondFaces = AsFaceList(meshes[1]);

      var backAnchored = new List<object>(firstFaces.Count + secondFaces.Count);
      backAnchored.AddRange(CloneFacesWithTranslation(firstFaces, 0, 0, 0));
      backAnchored.AddRange(CloneFacesWithTranslation(secondFaces, 1, 0, 0));

      var frontAnchored = new List<object>(firstFaces.Count + secondFaces.Count);
      frontAnchored.AddRange(CloneFacesWithTranslation(secondFaces, 0, 0, 0));
      frontAnchored.AddRange(CloneFacesWithTranslation(firstFaces, -1, 0, 0));

      return [backAnchored, frontAnchored];
    }

    return meshes;
  }

  private static List<Dictionary<string, object?>> AsFaceList(object meshObj)
  {
    return meshObj is List<object> mesh ? mesh.OfType<Dictionary<string, object?>>().ToList() : [];
  }

  private static IEnumerable<object> CloneFacesWithTranslation(
    IEnumerable<Dictionary<string, object?>> faces,
    double translateX,
    double translateY,
    double translateZ
  )
  {
    foreach (var face in faces)
    {
      var clone = new Dictionary<string, object?>(face, StringComparer.Ordinal);
      if (clone.TryGetValue("vertices", out var verticesObj) && verticesObj is double[][] vertices)
      {
        clone["vertices"] = vertices
          .Select(v => new[] { Round(v[0] + translateX), Round(v[1] + translateY), Round(v[2] + translateZ) })
          .ToArray();
      }

      if (clone.TryGetValue("min", out var minObj) && minObj is double[] min)
      {
        clone["min"] = new[] { Round(min[0] + translateX), Round(min[1] + translateY), Round(min[2] + translateZ) };
      }

      if (clone.TryGetValue("max", out var maxObj) && maxObj is double[] max)
      {
        clone["max"] = new[] { Round(max[0] + translateX), Round(max[1] + translateY), Round(max[2] + translateZ) };
      }

      yield return clone;
    }
  }

  private static bool TryReadModelIdFromSetBlockModelInvocation(
    InvocationExpressionSyntax invocation,
    out string modelId
  )
  {
    modelId = string.Empty;

    if (invocation.ArgumentList.Arguments.Count == 0)
      return false;

    var expression = Unwrap(invocation.ArgumentList.Arguments[0].Expression);
    if (expression is MemberAccessExpressionSyntax memberAccess)
    {
      modelId = memberAccess.Name.Identifier.Text;
      return !string.IsNullOrWhiteSpace(modelId);
    }

    return false;
  }

  private static bool IsBlockModelType(string typeName)
  {
    var rawTypeName = typeName.Split('.').Last().Trim();
    return rawTypeName.StartsWith("BlockModel", StringComparison.Ordinal);
  }

  private static string NormalizeTypeName(string typeName)
  {
    var rawTypeName = typeName.Split('.').Last().Trim();

    if (rawTypeName.StartsWith("BlockModel", StringComparison.Ordinal))
      return rawTypeName["BlockModel".Length..];

    return rawTypeName;
  }

  private sealed class CuboidDef
  {
    public int MinX { get; set; }
    public int MinY { get; set; }
    public int MinZ { get; set; }
    public int MaxX { get; set; }
    public int MaxY { get; set; }
    public int MaxZ { get; set; }
    public bool UseAo { get; set; }
    public int Flag { get; set; }
    public bool IgnorePaint { get; set; }
    public bool NoNormals { get; set; }
    public int[] TextureIndices { get; set; } = [0, 0, 0, 0, 0, 0];
    public (int X, int Y)[]? UvOffsets { get; set; }
    public double[]? Matrix { get; set; }
  }

  private readonly record struct FaceUvDef(double UMin, double VMin, double UMax, double VMax);

  private enum MultiBlockMode
  {
    None,
    FrontBack,
    TopBottom,
  }
}
