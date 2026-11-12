using System.Collections.Generic;
using System.Globalization;
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

    private static readonly DiagnosticDescriptor UnsupportedNormalization = new(
        "PNU1003",
        "Settings model cannot be normalized",
        "'{0}' sets Min/Max but is neither a record nor a struct, so the generated range check was skipped",
        "PhotinoNetUtils.Settings",
        DiagnosticSeverity.Warning,
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

            entries.Add(new Entry(key, comment!, order, property.Name, property.Type,
                NamedDouble(attribute, "Min"), NamedDouble(attribute, "Max")));
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

        AppendNormalizer(context, declarations, model, typeName, simpleName);

        if (model.Symbol.ContainingNamespace is { IsGlobalNamespace: false })
            declarations.AppendLine("}");

        context.AddSource(hint, SourceText.From(declarations.ToString(), Encoding.UTF8));
    }

    /// <summary>Emits the range checks behind <c>[Setting(Min = ..., Max = ...)]</c>.</summary>
    private static void AppendNormalizer(SourceProductionContext context, StringBuilder declarations, ModelInfo model,
        string typeName, string simpleName)
    {
        var clamped = model.Entries
            .Where(entry => entry.Min is not null || entry.Max is not null)
            .ToArray();

        if (clamped.Length == 0)
            return;

        var assignable = model.Entries
            .Where(entry => (entry.Min is not null || entry.Max is not null) && IsNumeric(entry.Type) &&
                            (model.Symbol.IsRecord || model.Symbol.IsValueType))
            .ToArray();

        if (assignable.Length == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedNormalization,
                model.Symbol.Locations.FirstOrDefault(), model.Symbol.Name));
            return;
        }

        declarations.AppendLine();
        declarations.AppendLine($"    internal static class {simpleName}_SettingsNormalizer");
        declarations.AppendLine("    {");
        declarations.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        declarations.AppendLine("        internal static void Register() =>");
        declarations.AppendLine("            global::PhotinoNetUtils.Settings.SettingsNormalizationRegistry.Register(");
        declarations.AppendLine($"                typeof({typeName}), Normalize);");
        declarations.AppendLine();
        declarations.AppendLine("        private static object Normalize(object value)");
        declarations.AppendLine("        {");
        declarations.AppendLine($"            var model = ({typeName})value;");
        declarations.AppendLine();
        declarations.AppendLine("            return model with");
        declarations.AppendLine("            {");
        declarations.AppendLine(string.Join(",\n", assignable.Select(entry =>
            $"                {entry.PropertyName} = global::System.Math.Clamp(model.{entry.PropertyName}, " +
            $"{Number(entry.Type, entry.Min ?? double.NegativeInfinity)}, " +
            $"{Number(entry.Type, entry.Max ?? double.PositiveInfinity)})")));
        declarations.AppendLine("            };");
        declarations.AppendLine("        }");
        declarations.AppendLine("    }");
    }

    private static bool IsNumeric(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Single
            or SpecialType.System_Double or SpecialType.System_Decimal;

    private static string Number(ITypeSymbol type, double value) =>
        double.IsInfinity(value)
            ? Bound(type, value > 0)
            : type.SpecialType switch
            {
                SpecialType.System_Int32 => ((int)value).ToString(CultureInfo.InvariantCulture),
                SpecialType.System_Int64 => ((long)value).ToString(CultureInfo.InvariantCulture) + "L",
                SpecialType.System_Single => ((float)value).ToString("R", CultureInfo.InvariantCulture) + "F",
                SpecialType.System_Decimal => ((decimal)value).ToString(CultureInfo.InvariantCulture) + "M",
                _ => value.ToString("R", CultureInfo.InvariantCulture) + "D"
            };

    /// <summary>The smallest or largest value of the property type, for a missing bound.</summary>
    private static string Bound(ITypeSymbol type, bool largest) =>
        type.SpecialType switch
        {
            SpecialType.System_Int32 => largest ? "int.MaxValue" : "int.MinValue",
            SpecialType.System_Int64 => largest ? "long.MaxValue" : "long.MinValue",
            SpecialType.System_Single => largest ? "float.MaxValue" : "float.MinValue",
            SpecialType.System_Decimal => largest ? "decimal.MaxValue" : "decimal.MinValue",
            _ => largest ? "double.MaxValue" : "double.MinValue"
        };

    private static string CamelCase(string name) =>
        char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static string? NamedString(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(item => item.Key == name).Value.Value as string;

    private static int NamedInt(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(item => item.Key == name).Value.Value is int value ? value : 0;

    private static double? NamedDouble(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(item => item.Key == name).Value.Value is double value &&
        !double.IsNaN(value)
            ? value
            : null;

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

    private sealed class Entry(
        string key,
        string comment,
        int order,
        string propertyName,
        ITypeSymbol type,
        double? min,
        double? max)
    {
        public string Key { get; } = key;
        public string Comment { get; } = comment;
        public int Order { get; } = order;
        public string PropertyName { get; } = propertyName;
        public ITypeSymbol Type { get; } = type;
        public double? Min { get; } = min;
        public double? Max { get; } = max;
    }
}
