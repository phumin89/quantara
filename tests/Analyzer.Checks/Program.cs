using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quantara.Analyzers;

static async Task<ImmutableArray<Diagnostic>> Analyze(string source, string filename)
{
    var tree = CSharpSyntaxTree.ParseText(source, path: filename);
    var compilation = CSharpCompilation.Create("OwnershipProbe", [tree], options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    return await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new FileOwnershipAnalyzer())).GetAnalyzerDiagnosticsAsync();
}

var invalid = await Analyze("public sealed record First(); public interface Second {}", "First.cs");
if (invalid.Count(item => item.Id == "QNT001") != 2 || invalid.Count(item => item.Id == "QNT002") != 1)
{
    throw new InvalidOperationException("File ownership analyzer did not reject multiple types and mismatched filenames.");
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
