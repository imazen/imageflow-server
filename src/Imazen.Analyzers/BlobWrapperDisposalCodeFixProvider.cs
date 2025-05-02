using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Imazen.Analyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlobWrapperDisposalCodeFixProvider)), Shared]
    public class BlobWrapperDisposalCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(BlobWrapperDisposalAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the node that triggered the diagnostic (invocation or creation)
            var declarationNode = root.FindNode(diagnosticSpan);
            if (declarationNode == null) return;


            // Find the local declaration statement if the value is assigned to a variable
            VariableDeclarationSyntax? variableDeclaration = null;
            if (declarationNode.Parent is EqualsValueClauseSyntax eq && eq.Parent is VariableDeclaratorSyntax vd && vd.Parent is VariableDeclarationSyntax vds)
            {
                 // Check if it's already within a using *statement* body (not declaration) - fix might not apply well
                 if (!(vds.Parent is UsingStatementSyntax))
                 {
                      variableDeclaration = vds;
                 }
            }
            // Handle direct assignment like 'blob = GetBlob();' - harder to fix automatically, might require manual using later
             else if (declarationNode.Parent is AssignmentExpressionSyntax assignment && assignment.Right == declarationNode)
             {
                 // Offer a fix only if the LHS is a local variable declaration nearby? Too complex for now.
                 return; // Don't offer automatic fix for simple assignment for now
             }
            // Handle case where invocation/creation is directly in a statement (e.g., GetBlob(); ) - likely an error anyway, no good fix.
            else if (declarationNode.Parent is ExpressionStatementSyntax)
            {
                 return; // Can't wrap a bare expression statement in using
            }


            if (variableDeclaration != null && variableDeclaration.Parent is LocalDeclarationStatementSyntax localDeclarationStatement)
            {
                // Ensure it's not already using 'using' modifier
                if (!localDeclarationStatement.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) && !localDeclarationStatement.Modifiers.Any(SyntaxKind.UsingKeyword))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: "Add 'using' keyword",
                            createChangedDocument: c => AddUsingKeywordAsync(context.Document, localDeclarationStatement, c),
                            equivalenceKey: "Add using keyword"),
                        diagnostic);
                 }
            }
             // Maybe handle the case where the expression is the direct expression of a using statement later?
        }

         private async Task<Document> AddUsingKeywordAsync(Document document, LocalDeclarationStatementSyntax localDeclaration, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            // Create the new statement with the using keyword
            var newLocalDeclaration = localDeclaration.WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword))
                                                       .WithLeadingTrivia(localDeclaration.GetLeadingTrivia()) // Preserve trivia
                                                       .WithTrailingTrivia(localDeclaration.GetTrailingTrivia());


            editor.ReplaceNode(localDeclaration, newLocalDeclaration);

            return editor.GetChangedDocument();
        }
    }
} 