using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Quantara.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MemberSpacingCodeFixProvider)), Shared]
public sealed class MemberSpacingCodeFixProvider : CodeFixProvider
{
    private const string diagnosticId = "QNT005";

    private const string title = "Add blank line between members";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(diagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var documentRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (documentRoot is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var member = documentRoot
                .FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .AncestorsAndSelf()
                .OfType<MemberDeclarationSyntax>()
                .FirstOrDefault();
            if (member is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => AddSpacingAsync(context.Document, member, cancellationToken),
                    title
                ),
                diagnostic
            );
        }
    }

    private static async Task<Document> AddSpacingAsync(
        Document document,
        MemberDeclarationSyntax member,
        CancellationToken cancellationToken
    )
    {
        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var lineEnding = sourceText.ToString().IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        var previousCharacterPosition = Math.Max(member.FullSpan.Start - 1, 0);
        var precedingText = sourceText.ToString(TextSpan.FromBounds(previousCharacterPosition, member.FullSpan.Start));
        var insertion = precedingText.IndexOf('\n') >= 0 ? lineEnding : lineEnding + lineEnding;
        var updatedText = sourceText.WithChanges(new TextChange(new TextSpan(member.FullSpan.Start, 0), insertion));
        return document.WithText(updatedText);
    }
}
