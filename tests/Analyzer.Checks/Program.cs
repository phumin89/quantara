using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quantara.Analyzers;
using Quantara.CodeFixes;

static async Task<ImmutableArray<Diagnostic>> Analyze(string source, string filename)
{
    var tree = CSharpSyntaxTree.ParseText(source, path: filename);
    var compilation = CSharpCompilation.Create(
        "OwnershipProbe",
        [tree],
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );
    return await compilation
        .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new FileOwnershipAnalyzer()))
        .GetAnalyzerDiagnosticsAsync();
}

var invalid = await Analyze("public sealed record First(); public interface Second {}", "First.cs");
if (invalid.Count(item => item.Id == "QNT001") != 2 || invalid.Count(item => item.Id == "QNT002") != 1)
{
    throw new InvalidOperationException(
        "File ownership analyzer did not reject multiple types and mismatched filenames."
    );
}

var valid = await Analyze("public sealed record First();", "First.cs");
if (valid.Length != 0)
{
    throw new InvalidOperationException("File ownership analyzer rejected a valid file.");
}

Console.WriteLine("C# editor analyzer rejects multiple types/mismatched filenames and accepts valid files.");
var unsafeSource = await Analyze("public class Unsafe { public string Read(string? value) => value!; }", "Unsafe.cs");
if (!unsafeSource.Any(item => item.Id == "QNT003") || !unsafeSource.Any(item => item.Id == "QNT004"))
{
    throw new InvalidOperationException("Null suppression or implicit inheritance was accepted.");
}

Console.WriteLine("C# editor analyzer rejects null suppression and implicit inheritance.");

var adjacentMembers = await Analyze(
    "public sealed class First\n{\n    private readonly int value;\n    public First(int value) { this.value = value; }\n}",
    "First.cs"
);
if (!adjacentMembers.Any(item => item.Id == "QNT005"))
{
    throw new InvalidOperationException("Adjacent field and constructor were accepted without a blank line.");
}

var spacedMembers = await Analyze(
    "public sealed class First\n{\n    private readonly int value;\n\n    public First(int value) { this.value = value; }\n}",
    "First.cs"
);
if (spacedMembers.Length != 0)
{
    throw new InvalidOperationException("Correctly separated type members were rejected.");
}

var commentedMembers = await Analyze(
    "public sealed class First\n{\n    public int Value => 1;\n    // A comment is not a blank line.\n    public int Other => 2;\n}",
    "First.cs"
);
if (!commentedMembers.Any(item => item.Id == "QNT005"))
{
    throw new InvalidOperationException("A comment incorrectly substituted for the blank line.");
}

Console.WriteLine("C# editor analyzer requires blank lines between type members, including fields and constructors.");

var inlineMembers = await Analyze(
    "public sealed class First { public int Value => 1; public int Other => 2; }",
    "First.cs"
);
if (!inlineMembers.Any(item => item.Id == "QNT005") || inlineMembers.Any(item => item.Id == "AD0001"))
{
    throw new InvalidOperationException(
        "Inline members must produce a spacing diagnostic without crashing the analyzer."
    );
}

var documentedMembers = await Analyze(
    "public sealed class First\n{\n    public int Value => 1;\n\n    /// <summary>Other value.</summary>\n    [System.Obsolete]\n    public int Other => 2;\n}",
    "First.cs"
);
if (documentedMembers.Length != 0)
{
    throw new InvalidOperationException("A blank line before member documentation and attributes should be accepted.");
}

foreach (var newline in new[] { "\n", "\r\n" })
{
    foreach (
        var source in new[]
        {
            "public sealed class First { public int Value => 1; public int Other => 2; }",
            "public sealed class First\n{\n    private readonly int value;\n    /// <summary>Retained documentation.</summary>\n    [System.Obsolete]\n    public First(int value) { this.value = value; }\n}",
        }
    )
    {
        var input = source.Replace("\n", newline, StringComparison.Ordinal) + newline;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("CodeFixProbe", LanguageNames.CSharp);
        var document = workspace.AddDocument(
            project.Id,
            "First.cs",
            Microsoft.CodeAnalysis.Text.SourceText.From(input)
        );
        var diagnostics = await Analyze(input, "First.cs");
        var actions = new List<CodeAction>();
        var provider = new MemberSpacingCodeFixProvider();
        await provider.RegisterCodeFixesAsync(
            new CodeFixContext(
                document,
                diagnostics.First(item => item.Id == "QNT005"),
                (action, _) => actions.Add(action),
                CancellationToken.None
            )
        );
        var operations = await actions.Single().GetOperationsAsync(CancellationToken.None);
        var updated =
            operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("Code fix lost its document.");
        var output = (await updated.GetTextAsync()).ToString();
        var beforeTokens = CSharpSyntaxTree.ParseText(input).GetRoot().DescendantTokens().Select(token => token.Text);
        var afterTokens = CSharpSyntaxTree.ParseText(output).GetRoot().DescendantTokens().Select(token => token.Text);
        if (
            !beforeTokens.SequenceEqual(afterTokens)
            || (await Analyze(output, "First.cs")).Any(item => item.Id == "QNT005")
        )
        {
            throw new InvalidOperationException("Member spacing fix must preserve tokens and remove its diagnostic.");
        }

        if (
            input.Contains("Retained documentation", StringComparison.Ordinal)
            && !output.Contains("/// <summary>Retained documentation.</summary>", StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException("Member documentation was lost by the code fix.");
        }

        if (newline == "\r\n" && output.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n'))
        {
            throw new InvalidOperationException("Code fix changed CRLF line endings.");
        }
    }
}

Console.WriteLine("Member spacing Quick Fix preserves tokens, documentation, attributes and LF/CRLF.");
