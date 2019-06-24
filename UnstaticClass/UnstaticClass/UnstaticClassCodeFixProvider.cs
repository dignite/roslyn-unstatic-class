using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace UnstaticClass
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnstaticClassCodeFixProvider)), Shared]
    public class UnstaticClassCodeFixProvider : CodeFixProvider
    {
        private const string title = "Make non-static with singleton";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(UnstaticClassAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedSolution: c => RemoveStaticAsync(context.Document.Project.Solution, declaration, c),
                    equivalenceKey: title),
                diagnostic);
        }

        private async Task<Solution> RemoveStaticAsync(Solution solution, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken)
        {
            var unstaticClassRewriter = new UnstaticClassRewriter(typeDecl);

            // Iterates through every project
            foreach (var project in solution.Projects)
            {
                // Iterates through every file
                foreach (var document in project.Documents)
                {
                    // Selects the syntax tree
                    var syntaxTree = await document.GetSyntaxTreeAsync();
                    var root = syntaxTree.GetRoot();

                    // Generates the syntax rewriter
                    root = unstaticClassRewriter.Visit(root);

                    // Exchanges the document in the solution by the newly generated document
                    solution = solution.WithDocumentSyntaxRoot(document.Id, root);
                }
            }

            return solution;
        }
    }

    public class UnstaticClassRewriter : CSharpSyntaxRewriter
    {
        private readonly TypeDeclarationSyntax typeDeclarationToUnstatic;

        public UnstaticClassRewriter(TypeDeclarationSyntax typeDeclarationToUnstatic)
        {
            this.typeDeclarationToUnstatic = typeDeclarationToUnstatic;
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if(node == typeDeclarationToUnstatic)
            {
                var classDeclarationWithoutStatic = node
                    .WithModifiers(new SyntaxTokenList(node.Modifiers.Where(m => m.ValueText != "static")))
                    .WithLeadingTrivia(node.GetLeadingTrivia())
                    .WithTrailingTrivia(node.GetTrailingTrivia());
                var unstaticMethods = new UnstaticStaticClassMethodsRewriter(classDeclarationWithoutStatic);
                return unstaticMethods.Visit(classDeclarationWithoutStatic);
            }

            return base.VisitClassDeclaration(node);
        }
    }

    public class UnstaticStaticClassMethodsRewriter : CSharpSyntaxRewriter
    {
        private readonly TypeDeclarationSyntax classToRewrite;

        public UnstaticStaticClassMethodsRewriter(TypeDeclarationSyntax classToReWrite)
        {
            this.classToRewrite = classToReWrite;
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Parent == classToRewrite)
            {
                return node
                    .WithModifiers(new SyntaxTokenList(node.Modifiers.Where(m => m.ValueText != "static")))
                    .WithLeadingTrivia(node.GetLeadingTrivia())
                    .WithTrailingTrivia(node.GetTrailingTrivia());
            }

            return base.VisitMethodDeclaration(node);
        }
    }


}
