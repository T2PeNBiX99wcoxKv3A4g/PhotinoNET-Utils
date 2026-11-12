using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PhotinoNetUtils.Generators;

/// <summary>
///     Reads <c>[SettingsModel]</c> / <c>[Setting]</c> and registers the comments in
///     <c>SettingsMetadataRegistry</c> from a module initializer. The TOML writer then has the
///     metadata without reflecting over the model.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SettingsModelGenerator : IIncrementalGenerator
{
    private const string ModelAttribute = "PhotinoNetUtils.Settings.SettingsModelAttribute";
    private const string SettingAttribute = "PhotinoNetUtils.Settings.SettingAttribute";

    private static readonly DiagnosticDescriptor NestedModel = new(
        "PNU1001",
        "Settings model cannot be nested or generic",
        "'{0}' must be a top-level, non-generic type to use [SettingsModel]",
        "PhotinoNetUtils.Settings",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor DuplicateKey = new(
        "PNU1002",
        "Duplicate setting key",
        "'{0}' has more than one setting with the key '{1}'",
        "PhotinoNetUtils.Settings",
        DiagnosticSeverity.Error,
        true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
                ModelAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => Build(ctx))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(models, static (spc, model) => Emit(spc, model));
    }

    private static ModelInfo? Build(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var diagnostics = new List<Diagnostic>();

        if (symbol.ContainingType is not null || symbol.TypeParameters.Length > 0)
        {
            diagnostics.Add(Diagnostic.Create(NestedModel, symbol.Locations.FirstOrDefault(), symbol.Name));
            return new ModelInfo(symbol, [], diagnostics);
        }

        var entries = new List<Entry>();

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            var attribute = property.GetAttributes()
                .FirstOrDefault(item => item.AttributeClass?.ToDisplayString() == SettingAttribute);

            if (attribute is null)
                continue;

            var comment = NamedString(attribute, "Comment");
            if (string.IsNullOrWhiteSpace(comment))
                continue;

            var key = NamedString(attribute, "Key") ?? CamelCase(property.Name);
            var order = NamedInt(attribute, "Order");

            entries.Add(new Entry(key, comment!, order));
        }

        var duplicate = entries.GroupBy(entry => entry.Key).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            diagnostics.Add(Diagnostic.Create(DuplicateKey, symbol.Locations.FirstOrDefault(), symbol.Name,
                duplicate.Key));

        return new ModelInfo(symbol, entries.OrderBy(entry => entry.Order).ToArray(), diagnostics);
    }

    private static void Emit(SourceProductionContext context, ModelInfo model)
    {
        foreach (var diagnostic in model.Diagnostics)
            context.ReportDiagnostic(diagnostic);

        if (model.Entries.Length == 0)
            return;

        var typeName = model.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var simpleName = model.Symbol.Name;
        var hint = $"{model.Symbol.ToDisplayString().Replace('.', '_')}.SettingsMetadata.g.cs";
        var declarations = new StringBuilder();

        if (model.Symbol.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            declarations.AppendLine($"namespace {ns.ToDisplayString()}");
            declarations.AppendLine("{");
        }

        declarations.AppendLine($"    internal static class {simpleName}_SettingsMetadata");
        declarations.AppendLine("    {");
        declarations.AppendLine("        private static readonly global::PhotinoNetUtils.Settings.SettingDefinition[] Definitions =");
        declarations.AppendLine("        [");
        foreach (var entry in model.Entries)
            declarations.AppendLine(
                $"            new global::PhotinoNetUtils.Settings.SettingDefinition({Literal(entry.Key)}, {Literal(entry.Comment)}),");
        declarations.AppendLine("        ];");
        declarations.AppendLine();
        declarations.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        declarations.AppendLine("        internal static void Register() =>");
        declarations.AppendLine("            global::PhotinoNetUtils.Settings.SettingsMetadataRegistry.Register(");
        declarations.AppendLine($"                typeof({typeName}), Definitions);");
        declarations.AppendLine("    }");

        if (model.Symbol.ContainingNamespace is { IsGlobalNamespace: false })
            declarations.AppendLine("}");

        context.AddSource(hint, SourceText.From(declarations.ToString(), Encoding.UTF8));
    }

    private static string CamelCase(string name) =>
        char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static string? NamedString(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(item => item.Key == name).Value.Value as string;

    private static int NamedInt(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(item => item.Key == name).Value.Value is int value ? value : 0;

    private static string Literal(string value) =>
        Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true);

    private sealed class ModelInfo(
        INamedTypeSymbol symbol,
        Entry[] entries,
        List<Diagnostic> diagnostics)
    {
        public INamedTypeSymbol Symbol { get; } = symbol;
        public Entry[] Entries { get; } = entries;
        public List<Diagnostic> Diagnostics { get; } = diagnostics;
    }

    private sealed class Entry(string key, string comment, int order)
    {
        public string Key { get; } = key;
        public string Comment { get; } = comment;
        public int Order { get; } = order;
    }
}
