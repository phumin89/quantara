using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = Directory.GetCurrentDirectory();
var sourceFiles = Directory
    .EnumerateFiles(Path.Combine(root, "services"), "*.cs", SearchOption.AllDirectories)
    .Where(path =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
    );
var checkedFiles = 0;
var writePorts = new HashSet<string>(StringComparer.Ordinal)
{
    "IPortfolioRepository",
    "IPortfolioGoalStore",
    "IOrderStore",
    "IOrderNotificationStore",
    "ITradingStore",
    "IRiskStore",
    "ITeamRunStore",
    "ICompanyScheduleStore",
    "ICompanyLoop",
    "IIntradayCandleStore",
    "IMarketEventBus",
    "IPortfolioSync",
    "ITradingExchange",
    "ITradingControl",
    "IOrderBroker",
    "IHumanOrderReview",
    "IOrderProposalPublisher",
    "ITeamCoordinator",
    "ICodexWorker",
    "ITelegramTransport",
};
foreach (var path in sourceFiles)
{
    var syntax = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(path)).GetRoot();
    if (
        syntax
            .DescendantNodes()
            .OfType<PostfixUnaryExpressionSyntax>()
            .Any(node => node.IsKind(SyntaxKind.SuppressNullableWarningExpression))
    )
    {
        throw new InvalidOperationException($"Do not suppress nullability; validate or model null instead: {path}");
    }

    var declarations = syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToArray();
    foreach (var declaration in declarations.OfType<ClassDeclarationSyntax>())
    {
        if (
            !declaration.Modifiers.Any(token =>
                token.IsKind(SyntaxKind.SealedKeyword)
                || token.IsKind(SyntaxKind.AbstractKeyword)
                || token.IsKind(SyntaxKind.StaticKeyword)
            )
        )
        {
            throw new InvalidOperationException($"Classes must be sealed unless explicitly abstract/static: {path}");
        }
    }

    if (declarations.Length > 1)
    {
        throw new InvalidOperationException($"One type per file required: {path}");
    }

    if (declarations.Length == 1 && declarations[0].Identifier.ValueText != Path.GetFileNameWithoutExtension(path))
    {
        throw new InvalidOperationException($"File name must match its type: {path}");
    }

    if (declarations.Length == 0 && Path.GetFileName(path) != "Program.cs")
    {
        throw new InvalidOperationException($"Unexpected type-free service file: {path}");
    }

    if (path.Contains(".Domain", StringComparison.Ordinal))
    {
        var imports = syntax
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(item => item.Name?.ToString() ?? "");
        if (
            imports.Any(name =>
                name.StartsWith("Microsoft", StringComparison.Ordinal)
                || name.StartsWith("Mediator", StringComparison.Ordinal)
                || name.StartsWith("System.Text.Json", StringComparison.Ordinal)
                || name.StartsWith("System.Net", StringComparison.Ordinal)
                || name.StartsWith("System.Data", StringComparison.Ordinal)
                || name.Contains(".Infrastructure", StringComparison.Ordinal)
                || name.Contains(".Application", StringComparison.Ordinal)
                || name.Contains(".Api", StringComparison.Ordinal)
            )
        )
        {
            throw new InvalidOperationException($"Domain has an external dependency: {path}");
        }

        if (
            syntax
                .DescendantNodes()
                .OfType<QualifiedNameSyntax>()
                .Any(item =>
                    item.ToString().StartsWith("System.Text.Json", StringComparison.Ordinal)
                    || item.ToString().StartsWith("System.Net", StringComparison.Ordinal)
                    || item.ToString().StartsWith("System.Data", StringComparison.Ordinal)
                )
        )
        {
            throw new InvalidOperationException($"Domain must not implement transport or persistence: {path}");
        }
    }

    if (
        path.EndsWith("Controller.cs", StringComparison.Ordinal)
        && syntax
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Any(item => (item.Name?.ToString() ?? "").Contains(".Infrastructure", StringComparison.Ordinal))
    )
    {
        throw new InvalidOperationException($"Controllers must delegate through Application/Mediator: {path}");
    }

    if (
        syntax.DescendantNodes().OfType<GenericNameSyntax>().Any(item => item.Identifier.ValueText == "IQueryHandler")
        && syntax
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(item => writePorts.Contains(item.Identifier.ValueText))
    )
    {
        throw new InvalidOperationException($"Query handlers must use read-only ports: {path}");
    }

    checkedFiles++;
}

foreach (
    var project in Directory.EnumerateFiles(Path.Combine(root, "services"), "*.csproj", SearchOption.AllDirectories)
)
{
    var name = Path.GetFileNameWithoutExtension(project);
    var document = XDocument.Load(project);
    var projectDirectory =
        Path.GetDirectoryName(project) ?? throw new InvalidOperationException("Project directory is missing.");
    var references = document
        .Descendants("ProjectReference")
        .Select(item =>
            Path.GetFullPath(
                Path.Combine(
                    projectDirectory,
                    (
                        item.Attribute("Include")
                        ?? throw new InvalidOperationException("Project reference path is missing.")
                    ).Value.Replace('\\', Path.DirectorySeparatorChar)
                )
            )
        )
        .ToArray();
    foreach (var reference in references)
    {
        if (
            !reference.StartsWith(
                Path.GetFullPath(Path.Combine(projectDirectory, "..")) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException(
                $"Services must communicate through HTTP, not project references: {project}"
            );
        }

        var referencedName = Path.GetFileNameWithoutExtension(reference);
        if (
            name.EndsWith(".Domain", StringComparison.Ordinal)
            || (
                name.EndsWith(".Application", StringComparison.Ordinal)
                && !referencedName.EndsWith(".Domain", StringComparison.Ordinal)
            )
            || (
                name.EndsWith(".Infrastructure", StringComparison.Ordinal)
                && referencedName.EndsWith(".Api", StringComparison.Ordinal)
            )
        )
        {
            throw new InvalidOperationException($"Invalid layer dependency: {project} -> {reference}");
        }
    }

    if (name.EndsWith(".Domain", StringComparison.Ordinal) && document.Descendants("PackageReference").Any())
    {
        throw new InvalidOperationException($"Domain must remain framework-free: {project}");
    }
}

Console.WriteLine($"Architecture: {checkedFiles} C# files satisfy type ownership and layer/service boundaries.");
