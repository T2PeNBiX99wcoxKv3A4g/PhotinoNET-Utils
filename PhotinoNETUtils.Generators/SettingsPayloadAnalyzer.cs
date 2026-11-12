using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PhotinoNetUtils.Generators;

/// <summary>
///     Checks a record marked <c>[SettingsPayload(typeof(MySettings))]</c> against the settings model:
///     every <c>[Setting]</c> the model declares must have a property on the payload, or the new
///     setting silently never reaches the interface.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SettingsPayloadAnalyzer : DiagnosticAnalyzer
{
    private const string PayloadAttribute = "PhotinoNetUtils.Settings.SettingsPayloadAttribute";
    private const string SettingAttribute = "PhotinoNetUtils.Settings.SettingAttribute";

    private static readonly DiagnosticDescriptor MissingProperty = new(
        "PNU1201",
        "Settings payload is missing a setting",
        "'{0}' has no property for the setting '{1}' of {2}",
        "PhotinoNetUtils.Settings",
        DiagnosticSeverity.Warning,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [MissingProperty];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(PayloadAttribute) is null)
                return;

            start.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var payload = (INamedTypeSymbol)context.Symbol;

        foreach (var attribute in payload.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != PayloadAttribute)
                continue;

            if (attribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol model)
                continue;

            var properties = new HashSet<string>(
                payload.GetMembers().OfType<IPropertySymbol>().Select(property => Normalize(property.Name)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var setting in model.GetMembers().OfType<IPropertySymbol>())
            {
                var marker = setting.GetAttributes()
                    .FirstOrDefault(item => item.AttributeClass?.ToDisplayString() == SettingAttribute);

                if (marker is null)
                    continue;

                var key = marker.NamedArguments.FirstOrDefault(item => item.Key == "Key").Value.Value as string;

                if (properties.Contains(Normalize(setting.Name)) ||
                    key is not null && properties.Contains(Normalize(key)))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(MissingProperty,
                    payload.Locations.FirstOrDefault(), payload.Name, setting.Name, model.Name));
            }
        }
    }

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}