using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SyntaxParsingHelpers
{
  public static CompilationUnitSyntax ParseCompilationUnit(string path)
  {
    var text = File.ReadAllText(path);
    return CSharpSyntaxTree.ParseText(text, path: path).GetCompilationUnitRoot();
  }

  public static IEnumerable<FieldDeclarationSyntax> FindPublicStaticFields(CompilationUnitSyntax root)
  {
    return root.DescendantNodes().OfType<FieldDeclarationSyntax>().Where(IsPublicStaticField);
  }

  public static IEnumerable<InvocationExpressionSyntax> FindInvocations(ExpressionSyntax expression)
  {
    return expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>();
  }

  public static ObjectCreationExpressionSyntax? TryGetRootObjectCreation(ExpressionSyntax expression)
  {
    ExpressionSyntax? cursor = expression;

    while (cursor is not null)
    {
      switch (cursor)
      {
        case ObjectCreationExpressionSyntax creation:
          return creation;
        case CastExpressionSyntax cast:
          cursor = cast.Expression;
          break;
        case ParenthesizedExpressionSyntax parenthesized:
          cursor = parenthesized.Expression;
          break;
        case InvocationExpressionSyntax invocation
          when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
          cursor = memberAccess.Expression;
          break;
        case MemberAccessExpressionSyntax memberAccess:
          cursor = memberAccess.Expression;
          break;
        default:
          return cursor.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
      }
    }

    return null;
  }

  public static string? TryReadIdFromObjectCreation(ObjectCreationExpressionSyntax? creation)
  {
    var firstArg = creation?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
    if (firstArg is null)
    {
      return null;
    }

    if (firstArg is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
    {
      return literal.Token.ValueText;
    }

    if (firstArg is InvocationExpressionSyntax invocation && GetInvocationName(invocation) == "nameof")
    {
      var nameofArg = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
      return nameofArg switch
      {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
        _ => null,
      };
    }

    return null;
  }

  public static string? TryReadQualifiedMemberArg(
    InvocationExpressionSyntax invocation,
    int argumentIndex,
    string expectedOwner
  )
  {
    if (invocation.ArgumentList.Arguments.Count <= argumentIndex)
    {
      return null;
    }

    return TryReadMemberName(invocation.ArgumentList.Arguments[argumentIndex].Expression, expectedOwner);
  }

  public static string? TryReadMemberName(ExpressionSyntax expression, string expectedOwner)
  {
    return
      expression is MemberAccessExpressionSyntax memberAccess
      && memberAccess.Expression is IdentifierNameSyntax owner
      && owner.Identifier.Text == expectedOwner
      ? memberAccess.Name.Identifier.Text
      : null;
  }

  public static int? TryReadIntArg(InvocationExpressionSyntax invocation, int argumentIndex)
  {
    if (invocation.ArgumentList.Arguments.Count <= argumentIndex)
    {
      return null;
    }

    return TryParseInt(invocation.ArgumentList.Arguments[argumentIndex].Expression);
  }

  public static int? TryParseInt(ExpressionSyntax expression)
  {
    return expression switch
    {
      LiteralExpressionSyntax literal when literal.Token.Value is int value => value,
      PrefixUnaryExpressionSyntax prefix
        when prefix.IsKind(SyntaxKind.UnaryMinusExpression)
          && prefix.Operand is LiteralExpressionSyntax operand
          && operand.Token.Value is int positive => -positive,
      _ => null,
    };
  }

  public static string? GetInvocationName(InvocationExpressionSyntax invocation)
  {
    return invocation.Expression switch
    {
      IdentifierNameSyntax identifier => identifier.Identifier.Text,
      MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
      _ => null,
    };
  }

  public static int GetLineNumber(SyntaxNode node)
  {
    return node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
  }

  public static int GetLineNumber(VariableDeclaratorSyntax variable)
  {
    return variable.SyntaxTree.GetLineSpan(variable.Identifier.Span).StartLinePosition.Line + 1;
  }

  private static bool IsPublicStaticField(FieldDeclarationSyntax field)
  {
    var hasPublic = field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword));
    var hasStatic = field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword));
    return hasPublic && hasStatic;
  }
}

