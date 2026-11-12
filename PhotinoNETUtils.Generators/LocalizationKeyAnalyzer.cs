using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace PhotinoNetUtils.Generators;

/// <summary>
///     Reports <c>Localization.Get("key")</c> calls whose key is missing from the translation files
///     added as <c>AdditionalFiles</c>. Without this a typo only shows up as the raw key in the UI.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LocalizationKeyAnalyzer : DiagnosticAnalyzer
{
    private const string Localization = "PhotinoNetUtils.Localization.Localization";

    private static readonly DiagnosticDescriptor UnknownKey = new(
        "PNU2101",
        "Unknown localization key",
        "'{0}' is not in the embedded translation files",
        "PhotinoNetUtils.Localization",
        DiagnosticSeverity.Warning,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UnknownKey];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(Localization) is null)
                return;

            var keys = new HashSet<string>(
                start.Options.AdditionalFiles
                    .Where(file => LocalizationKeys.IsTranslationFile(file.Path))
                    .SelectMany(file => LocalizationKeys.Read(file.GetText(start.CancellationToken)?.ToString())),
                StringComparer.OrdinalIgnoreCase);

            if (keys.Count == 0)
                return;

            start.RegisterOperationAction(operation => Analyze(operation, keys), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, HashSet<string> keys)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name != "Get" || method.ContainingType?.ToDisplayString() != Localization)
            return;

        var key = invocation.Arguments.LastOrDefault()?.Value;
        if (key is not { ConstantValue: { HasValue: true, Value: string value } } || keys.Contains(value))
            return;

        context.ReportDiagnostic(Diagnostic.Create(UnknownKey, key.Syntax.GetLocation(), value));
    }
}
