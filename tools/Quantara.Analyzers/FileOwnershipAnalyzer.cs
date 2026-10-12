using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quantara.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileOwnershipAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor oneType = new(
        "QNT001",
        "One type per file",
        "File must contain only one declared type",
        "Architecture",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor matchingName = new(
        "QNT002",
        "Type and file names must match",
        "Type '{0}' must be declared in '{0}.cs'",
        "Architecture",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor nullSuppression = new(
        "QNT003",
        "Model or validate null",
        "Validate null instead of suppressing nullability with '!'",
        "Safety",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor sealedClass = new(
        "QNT004",
        "Explicit inheritance intent",
        "Class must be sealed unless explicitly abstract or static",
        "Architecture",
        DiagnosticSeverity.Error,
        true
    );

    private static readonly DiagnosticDescriptor memberSpacing = new(
        "QNT005",
        "Separate type members",
        "Add a blank line between type members",
        "Layout",
        DiagnosticSeverity.Error,
        true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(oneType, matchingName, nullSuppression, sealedClass, memberSpacing);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(Analyze);
    }

    private static void Analyze(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (
            var suppression in root.DescendantNodes()
                .OfType<PostfixUnaryExpressionSyntax>()
                .Where(node => node.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        )
        {
            context.ReportDiagnostic(Diagnostic.Create(nullSuppression, suppression.GetLocation()));
        }

        var declarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToArray();
        foreach (var declaration in declarations)
        {
            if (declaration is TypeDeclarationSyntax typeDeclaration)
            {
                var text = context.Tree.GetText(context.CancellationToken);
                for (var index = 1; index < typeDeclaration.Members.Count; index++)
                {
                    var previous = typeDeclaration.Members[index - 1];
                    var current = typeDeclaration.Members[index];
                    if (previous.Span.IsEmpty || current.Span.IsEmpty)
                    {
                        continue;
                    }

                    var start = text.Lines.GetLineFromPosition(previous.Span.End - 1).LineNumber + 1;
                    var end = text.Lines.GetLineFromPosition(current.SpanStart).LineNumber;
                    if (
                        !Enumerable
                            .Range(start, System.Math.Max(0, end - start))
                            .Any(line => string.IsNullOrWhiteSpace(text.Lines[line].ToString()))
                    )
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(memberSpacing, current.GetFirstToken().GetLocation())
                        );
                    }
                }
            }

            if (
                declaration is ClassDeclarationSyntax classDeclaration
                && !classDeclaration.Modifiers.Any(token =>
                    token.IsKind(SyntaxKind.SealedKeyword)
                    || token.IsKind(SyntaxKind.AbstractKeyword)
                    || token.IsKind(SyntaxKind.StaticKeyword)
                )
            )
            {
                context.ReportDiagnostic(Diagnostic.Create(sealedClass, declaration.Identifier.GetLocation()));
            }

            if (declarations.Length > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(oneType, declaration.Identifier.GetLocation()));
            }

            if (declaration.Identifier.ValueText != Path.GetFileNameWithoutExtension(context.Tree.FilePath))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        matchingName,
                        declaration.Identifier.GetLocation(),
                        declaration.Identifier.ValueText
                    )
                );
            }
        }
    }
}
