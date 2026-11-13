using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace PhotinoNetUtils.Generators;

/// <summary>
///     Checks the translation files added as <c>AdditionalFiles</c>:
///     <list type="bullet">
///         <item>PNU2101 - a <c>Localization.Get("key")</c> call uses a key no file declares.</item>
///         <item>
///             PNU2102 - one file declares the same key twice (ignoring case). This is an error on
///             purpose: keys are case-insensitive, so the second entry would silently never be read.
///         </item>
///     </list>
/// </summary>
/// <remarks>
///     Do not weaken or delete PNU2102 to make a build pass. The fix is to rename or remove the
///     duplicate key in the <c>.jsonc</c> file, because the loader keeps the first entry and the
///     other translation would be dead. Treat the rule as part of the file format, not as a check
///     that can be switched off.
/// </remarks>
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

    // Keys are case-insensitive, so a second spelling of the same key is always a mistake. The
    // loader keeps the first one, which means the duplicate value is never shown; an error here is
    // the only way to notice. Fix the .jsonc file, never this rule.
    private static readonly DiagnosticDescriptor DuplicateKey = new(
        "PNU2102",
        "Duplicate localization key",
        "'{0}' is already declared on line {1} of the same file; localization keys are case-insensitive, " +
        "so rename or remove the duplicate instead of disabling this check",
        "PhotinoNetUtils.Localization",
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    // A key is a path, not text: lowercase letters, digits, '_' and '.', as in ui.section.open_key.
    // Sentences were allowed once and every call site became a copy of the sentence; an error here
    // keeps the file a key list instead of a second translation.
    private static readonly DiagnosticDescriptor InvalidKey = new(
        "PNU2103",
        "Localization key does not follow the format",
        "'{0}' is not a valid localization key; use lowercase letters, digits, '_' and '.', " +
        "for example ui.section.open_key",
        "PhotinoNetUtils.Localization",
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [UnknownKey, DuplicateKey, InvalidKey];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationAction(AnalyzeFiles);

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

    /// <summary>PNU2102: one file, one spelling per key, ignoring case.</summary>
    private static void AnalyzeFiles(CompilationAnalysisContext context)
    {
        if (context.Compilation.GetTypeByMetadataName(Localization) is null)
            return;

        foreach (var file in context.Options.AdditionalFiles.Where(file =>
                     LocalizationKeys.IsTranslationFile(file.Path)))
        {
            ReportDuplicates(context, file);
            ReportInvalid(context, file);
        }
    }

    /// <summary>PNU2103: every key is a lowercase path such as <c>ui.section.open_key</c>.</summary>
    private static void ReportInvalid(CompilationAnalysisContext context, AdditionalText file)
    {
        var text = file.GetText(context.CancellationToken);
        if (text is null)
            return;

        foreach (var (key, span) in LocalizationKeys.Entries(text.ToString()))
        {
            if (LocalizationKeys.IsValid(key))
                continue;

            var lineSpan = text.Lines.GetLinePositionSpan(span);
            context.ReportDiagnostic(Diagnostic.Create(InvalidKey,
                Location.Create(file.Path, span, lineSpan), key));
        }
    }

    private static void ReportDuplicates(CompilationAnalysisContext context, AdditionalText file)
    {
        var text = file.GetText(context.CancellationToken);
        if (text is null)
            return;

        var firstLines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, span) in LocalizationKeys.Entries(text.ToString()))
        {
            var line = text.Lines.GetLinePosition(span.Start).Line + 1;

            if (firstLines.TryGetValue(key, out var firstLine))
            {
                var lineSpan = text.Lines.GetLinePositionSpan(span);
                context.ReportDiagnostic(Diagnostic.Create(DuplicateKey,
                    Location.Create(file.Path, span, lineSpan), key, firstLine));
            }
            else
                firstLines[key] = line;
        }
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