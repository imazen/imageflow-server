using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Imazen.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class BlobWrapperDisposalAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "IMA001";
        private const string Title = "Potential disposable Imazen object leak";
        private const string MessageFormat = "'{0}' creates/returns a disposable object that may not be disposed";
        private const string Description = "Imazen.Abstractions disposable objects (like IBlobWrapper, IConsumableBlob) must be disposed, typically via a 'using' statement or by returning them.";
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        // Define the fully qualified names of the types to track
        private static readonly ImmutableHashSet<string> TrackedTypeNames = ImmutableHashSet.Create(
            "Imazen.Abstractions.Blobs.IBlobWrapper",
            "Imazen.Abstractions.Blobs.BlobWrapper",
            "Imazen.Abstractions.Blobs.IConsumableBlob",
            "Imazen.Abstractions.Blobs.IConsumableMemoryBlob",
            "Imazen.Abstractions.Blobs.BlobResult", // DisposableResult<IBlobWrapper, HttpStatus>
            "Imazen.Abstractions.Resulting.IDisposableResult`2" // Generic, check type args later
        );

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Analyze method invocations and object creations
            context.RegisterSyntaxNodeAction(AnalyzeCreationOrInvocation, SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression);
        }

        private void AnalyzeCreationOrInvocation(SyntaxNodeAnalysisContext context)
        {
            ITypeSymbol? createdOrReturnedTypeSymbol = null;

            // Get the type being created or returned by the invocation
            if (context.Node is InvocationExpressionSyntax invocation)
            {
                var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
                createdOrReturnedTypeSymbol = methodSymbol?.ReturnType;
            }
            else if (context.Node is ObjectCreationExpressionSyntax creation)
            {
                createdOrReturnedTypeSymbol = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
            }

            if (createdOrReturnedTypeSymbol == null) return;

            // Check if the type is one we care about
            if (!IsTrackedDisposableType(createdOrReturnedTypeSymbol)) return;

            // Check how the created/returned value is used
            if (!IsSafelyHandled(context.Node, context.SemanticModel))
            {
                var displayNode = context.Node is InvocationExpressionSyntax inv ? (ExpressionSyntax)inv.Expression : context.Node;
                 if (displayNode is MemberAccessExpressionSyntax mae) // Prefer showing MethodName over Full.Class.MethodName
                {
                    displayNode = mae.Name;
                }
                else if (context.Node is ObjectCreationExpressionSyntax oce)
                {
                     displayNode = oce.Type;
                }

                var diagnostic = Diagnostic.Create(Rule, context.Node.GetLocation(), displayNode.ToString());
                context.ReportDiagnostic(diagnostic);
            }
        }

        private bool IsTrackedDisposableType(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null) return false;

            // Handle generic IDisposableResult<T, E>
            if (typeSymbol.OriginalDefinition?.ToString() == "Imazen.Abstractions.Resulting.IDisposableResult<TResult, TError>" && typeSymbol is INamedTypeSymbol namedType)
            {
                 // Check if the TResult type argument is one of our tracked base types
                 if (namedType.TypeArguments.Length > 0 && IsTrackedDisposableType(namedType.TypeArguments[0]))
                 {
                     return true;
                 }
            }

            // Check specific type names (including interfaces this type implements)
            var typeName = typeSymbol.OriginalDefinition.ToDisplayString(); // Use OriginalDefinition for generics
            if (TrackedTypeNames.Contains(typeName)) return true;

            foreach (var iface in typeSymbol.AllInterfaces)
            {
                if (TrackedTypeNames.Contains(iface.OriginalDefinition.ToDisplayString())) return true;
            }

            return false;
        }


        private bool IsSafelyHandled(SyntaxNode node, SemanticModel semanticModel)
        {
            var currentNode = node;
            while (currentNode != null && currentNode.Parent != null)
            {
                // 1. Assigned to a variable in a 'using' statement/declaration?
                if (currentNode.Parent is EqualsValueClauseSyntax equalsClause &&
                    equalsClause.Parent is VariableDeclaratorSyntax variableDeclarator &&
                    variableDeclarator.Parent is VariableDeclarationSyntax variableDeclaration)
                {
                     // Check if it's part of a LocalDeclarationStatementSyntax first
                     if (variableDeclaration.Parent is LocalDeclarationStatementSyntax localDecl)
                     {
                        // Using declaration (using var x = ...) C# 8+ style
                        if (localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
                           return true;

                        // Check Modifiers on LocalDeclarationStatementSyntax (localDecl), not VariableDeclarationSyntax
                        if (localDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.UsingKeyword)))
                             return true;

                        // Using statement (using (var x = ...)) older style
                        if (localDecl.Parent is UsingStatementSyntax usingStatementDecl && usingStatementDecl.Declaration == variableDeclaration)
                            return true;
                     }
                     // Check if it's a simple 'using (var x = ...)' - This case might be redundant with the above check? Keep for safety.
                     else if (variableDeclaration.Parent is UsingStatementSyntax usingStmt && usingStmt.Declaration == variableDeclaration)
                     {
                        return true;
                     }


                    // Check if the declared variable is later used in 'using (expr)'
                    if (IsVariableUsedInUsingStatement(variableDeclarator, semanticModel))
                       return true;

                }

                // 2. Part of a 'using' statement expression (e.g., using (GetBlob())) ?
                if (currentNode.Parent is UsingStatementSyntax usingStatement && usingStatement.Expression == currentNode)
                    return true;


                // 3. Returned directly from the method/lambda?
                if (currentNode.Parent is ReturnStatementSyntax)
                    return true;

                // 4. Passed as an argument? (We assume this transfers ownership - potentially refine later)
                if (currentNode.Parent is ArgumentSyntax)
                    return true;

                // 5. Arrow expression clause (e.g., () => GetBlob())?
                 if (currentNode.Parent is ArrowExpressionClauseSyntax)
                     return true;


                // Stop if we hit a statement boundary without a safe pattern
                if (currentNode is StatementSyntax && !(currentNode is BlockSyntax)) // Don't stop just for blocks
                     break; // Reached statement boundary without being handled

                 // Stop if used in an assignment where the LHS isn't immediately used in a using
                 if (currentNode.Parent is AssignmentExpressionSyntax assign && assign.Right == currentNode)
                 {
                     // Check if the variable being assigned to is later used in a using statement
                     ISymbol? assignedSymbol = semanticModel.GetSymbolInfo(assign.Left).Symbol;
                     if (assignedSymbol is ILocalSymbol localSymbol)
                     {
                         // Fix 3: Add null check for assign.Parent before accessing AncestorsAndSelf
                         var containingStatement = assign.Parent?.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
                         if (IsVariableUsedInUsingStatementSyntax(localSymbol, containingStatement, semanticModel))
                             return true;
                     }
                     break; // Assignment without immediate using found
                 }

                currentNode = currentNode.Parent;
            }

            return false; // No safe handling pattern found up the tree
        }


         // Helper to check if a variable declared by 'declarator' is used in a subsequent using statement
         private bool IsVariableUsedInUsingStatement(VariableDeclaratorSyntax declarator, SemanticModel semanticModel)
         {
             ISymbol? declaredSymbol = semanticModel.GetDeclaredSymbol(declarator);
             if (declaredSymbol == null) return false;

             // Find the containing statement block (or method body)
             var statementContainer = declarator.Ancestors().OfType<BlockSyntax>().FirstOrDefault()
                 ?? declarator.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Body
                 ?? declarator.Ancestors().OfType<AccessorDeclarationSyntax>().FirstOrDefault()?.Body
                 ?? declarator.Ancestors().OfType<LocalFunctionStatementSyntax>().FirstOrDefault()?.Body;


             if (statementContainer == null) return false;

             // Search statements *after* the declaration
             bool declarationFound = false;
             foreach (var statement in statementContainer.Statements)
             {
                  if (!declarationFound)
                  {
                       if (statement.DescendantNodesAndSelf().Contains(declarator))
                       {
                           declarationFound = true;
                       }
                       continue;
                  }

                 // Check if this statement is a 'using' statement using our variable
                 if (statement is UsingStatementSyntax usingStmt && usingStmt.Expression is IdentifierNameSyntax idName)
                 {
                     ISymbol? usedSymbol = semanticModel.GetSymbolInfo(idName).Symbol;
                     if (SymbolEqualityComparer.Default.Equals(declaredSymbol, usedSymbol))
                     {
                         return true;
                     }
                 }
                 //Could add checks for Dispose() calls here too, but it's complex to track reliably
             }

             return false;
         }
          // Helper to check variable usage within its scope starting from a specific point
         private bool IsVariableUsedInUsingStatementSyntax(ILocalSymbol localSymbol, StatementSyntax? startStatement, SemanticModel semanticModel)
         {
             if (startStatement == null) return false;

             var scope = startStatement.Parent as BlockSyntax;
             if (scope == null) return false;

             bool startFound = false;
             foreach (var statement in scope.Statements)
             {
                 if (statement == startStatement)
                 {
                     startFound = true;
                     continue;
                 }
                 if (!startFound) continue;

                 // Check descendant nodes for using statement or Dispose call on the symbol
                 foreach (var node in statement.DescendantNodesAndSelf())
                 {
                     // Is it used in a using statement expression?
                     if (node.Parent is UsingStatementSyntax usingStmt && usingStmt.Expression == node)
                     {
                          if (node is IdentifierNameSyntax idName)
                          {
                               ISymbol? usedSymbol = semanticModel.GetSymbolInfo(idName).Symbol;
                               if (SymbolEqualityComparer.Default.Equals(localSymbol, usedSymbol))
                               {
                                   return true;
                               }
                          }
                     }
                     // Add check for explicit Dispose() call if desired (complex due to control flow)
                 }
             }
             return false;
         }
    }
} 