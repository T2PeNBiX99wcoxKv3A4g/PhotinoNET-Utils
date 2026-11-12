using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PhotinoNetUtils.Generators;

/// <summary>
///     Turns <c>[NativeMessage("type")]</c> methods into one <c>HandleAsync</c> switch that implements
///     <c>INativeMessageHandler</c> on the containing partial class, so services do not repeat the
///     stringly-typed dispatch and never hold a generated method name of their own.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class NativeMessageGenerator : IIncrementalGenerator
{
    private const string Attribute = "PhotinoNetUtils.Messaging.NativeMessageAttribute";
    private const string Context = "PhotinoNetUtils.Messaging.NativeContext";
    private const string Response = "PhotinoNetUtils.Messaging.NativeResponse";

    private static readonly DiagnosticDescriptor NotPartial = new(
        "PNU1101",
        "Native message class must be partial",
        "'{0}' must be a partial class to receive the generated native message dispatcher",
        "PhotinoNetUtils.Messaging",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        "PNU1102",
        "Native message class is not supported",
        "'{0}' must be a top-level, non-generic, non-static class",
        "PhotinoNetUtils.Messaging",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor BadSignature = new(
        "PNU1103",
        "Native message method has an unsupported signature",
        "'{0}' must take one NativeContext and return NativeResponse? or Task<NativeResponse?>",
        "PhotinoNetUtils.Messaging",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor DuplicateType = new(
        "PNU1104",
        "Duplicate native message type",
        "'{0}' handles '{1}' more than once",
        "PhotinoNetUtils.Messaging",
        DiagnosticSeverity.Error,
        true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
                Attribute,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => Build(ctx))
            .Where(static handler => handler is not null)
            .Select(static (handler, _) => handler!)
            .Collect();

        var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            new GeneratorOptions(
                provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns) ? ns : ""));

        var input = methods
            .Combine(context.CompilationProvider)
            .Combine(options);

        context.RegisterSourceOutput(input, static (spc, item) =>
            Emit(spc, item.Left.Left, item.Left.Right, item.Right));
    }

    private static HandlerInfo? Build(GeneratorAttributeSyntaxContext context)
    {
        var method = (IMethodSymbol)context.TargetSymbol;
        var diagnostics = new List<Diagnostic>();
        var containing = method.ContainingType;

        if (containing is not { TypeKind: TypeKind.Class } || containing.IsStatic ||
            containing.ContainingType is not null || containing.TypeParameters.Length > 0)
        {
            diagnostics.Add(Diagnostic.Create(UnsupportedType, method.Locations.FirstOrDefault(), containing?.Name));
            return new HandlerInfo(containing, method, "", false, diagnostics);
        }

        if (!IsPartial(containing))
            diagnostics.Add(Diagnostic.Create(NotPartial, containing.Locations.FirstOrDefault(), containing.Name));

        var messageType = context.Attributes.FirstOrDefault()?.ConstructorArguments.FirstOrDefault().Value as string ?? "";
        if (messageType.Length == 0 ||
            method.Parameters.Length != 1 ||
            method.Parameters[0].Type.ToDisplayString().TrimEnd('?') != Context)
        {
            diagnostics.Add(Diagnostic.Create(BadSignature, method.Locations.FirstOrDefault(), method.Name));
            return new HandlerInfo(containing, method, messageType, false, diagnostics);
        }

        if (method.ReturnType.ToDisplayString().TrimEnd('?') == Response)
            return new HandlerInfo(containing, method, messageType, false, diagnostics);

        if (method.ReturnType is INamedTypeSymbol task &&
            task.Name == "Task" &&
            task.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
            task.TypeArguments.Length == 1 &&
            task.TypeArguments[0].ToDisplayString().TrimEnd('?') == Response)
            return new HandlerInfo(containing, method, messageType, true, diagnostics);

        diagnostics.Add(Diagnostic.Create(BadSignature, method.Locations.FirstOrDefault(), method.Name));
        return new HandlerInfo(containing, method, messageType, false, diagnostics);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<HandlerInfo> handlers,
        Compilation compilation, GeneratorOptions options)
    {
        foreach (var diagnostics in handlers.SelectMany(handler => handler.Diagnostics))
            context.ReportDiagnostic(diagnostics);

        var classes = new List<INamedTypeSymbol>();

        foreach (var group in handlers
                     .Where(handler => handler.MessageType.Length > 0)
                     .GroupBy(handler => handler.Containing, SymbolEqualityComparer.Default))
        {
            var type = (INamedTypeSymbol?)group.Key;
            if (type is null)
                continue;

            var duplicate = group.GroupBy(handler => handler.MessageType)
                .FirstOrDefault(item => item.Count() > 1);

            if (duplicate is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateType, type.Locations.FirstOrDefault(),
                    type.Name, duplicate.Key));
                continue;
            }

            classes.Add(type);

            var source = BuildSource(type, group.ToArray());
            context.AddSource($"{type.ToDisplayString().Replace('.', '_')}.NativeMessages.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }

        EmitBootstrap(context, classes, compilation, options);
    }

    /// <summary>
    ///     Emits the <c>NativeMessages</c> entry point for an executable: one call connects every
    ///     class in this assembly that has a <c>[NativeMessage]</c> method, so no class needs a
    ///     constructor call of its own. A library gets none; its own services keep their wiring.
    /// </summary>
    private static void EmitBootstrap(SourceProductionContext context, List<INamedTypeSymbol> classes,
        Compilation compilation, GeneratorOptions options)
    {
        if (classes.Count == 0 ||
            compilation.Options.OutputKind is not (OutputKind.ConsoleApplication or OutputKind.WindowsApplication))
            return;

        var parameters = new List<string>();
        var bodies = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in classes)
        {
            var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (IsSingleton(type))
            {
                bodies.Add(
                    $"            global::PhotinoNetUtils.Messaging.NativeBridge.Subscribe({typeName}.Instance);");
                continue;
            }

            var name = UniqueName(names, CamelCase(type.Name));
            parameters.Add($"{typeName} {name}");
            bodies.Add($"            global::PhotinoNetUtils.Messaging.NativeBridge.Subscribe({name});");
        }

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");

        var indent = "";

        if (options.RootNamespace.Length > 0)
        {
            builder.AppendLine($"namespace {options.RootNamespace}");
            builder.AppendLine("{");
            indent = "    ";
        }

        builder.AppendLine(
            $"{indent}/// <summary>Connects every [NativeMessage] class in this assembly. Call once from the entry point.</summary>");
        builder.AppendLine($"{indent}internal static class NativeMessages");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    private static bool _initialized;");
        builder.AppendLine();
        builder.AppendLine($"{indent}    public static void Initialize({string.Join(", ", parameters)})");
        builder.AppendLine($"{indent}    {{");
        builder.AppendLine($"{indent}        if (_initialized) return;");
        builder.AppendLine($"{indent}        _initialized = true;");
        builder.AppendLine();

        foreach (var body in bodies)
            builder.AppendLine(body);

        builder.AppendLine($"{indent}    }}");
        builder.AppendLine($"{indent}}}");

        if (options.RootNamespace.Length > 0)
            builder.AppendLine("}");

        context.AddSource("NativeMessages.Bootstrap.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    /// <summary>True for a type that derives from <c>Singleton&lt;T&gt;</c> with itself as the argument.</summary>
    private static bool IsSingleton(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current is { Name: "Singleton", IsGenericType: true, TypeArguments.Length: 1 } &&
                current.ContainingNamespace.ToDisplayString() == "PhotinoNetUtils.Utils" &&
                SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], type))
                return true;

        return false;
    }

    private static string UniqueName(HashSet<string> names, string name)
    {
        var candidate = name;
        var suffix = 2;
        while (!names.Add(candidate))
            candidate = name + suffix++;

        return candidate;
    }

    private static string CamelCase(string name) =>
        name.Length == 0 ? "service" : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private sealed class GeneratorOptions(string rootNamespace)
    {
        public string RootNamespace { get; } = rootNamespace;
    }

    private static string BuildSource(INamedTypeSymbol type, HandlerInfo[] handlers)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");

        if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            builder.AppendLine($"namespace {ns.ToDisplayString()}");
            builder.AppendLine("{");
        }

        var accessibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        var indent = type.ContainingNamespace is { IsGlobalNamespace: false } ? "    " : "";

        builder.AppendLine(
            $"{indent}{accessibility} partial class {type.Name} : global::PhotinoNetUtils.Messaging.INativeMessageHandler");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine(
            $"{indent}    public global::System.Threading.Tasks.Task<global::PhotinoNetUtils.Messaging.NativeResponse?> HandleAsync(");
        builder.AppendLine($"{indent}        global::PhotinoNetUtils.Messaging.NativeContext context) =>");
        builder.AppendLine($"{indent}        context.Request.Type switch");
        builder.AppendLine($"{indent}        {{");

        foreach (var handler in handlers)
        {
            var call = $"{handler.Method.Name}(context)";
            var value = handler.ReturnsTask
                ? call
                : $"global::System.Threading.Tasks.Task.FromResult<global::PhotinoNetUtils.Messaging.NativeResponse?>({call})";

            builder.AppendLine(
                $"{indent}            {Literal(handler.MessageType)} => {value},");
        }

        builder.AppendLine(
            $"{indent}            _ => global::System.Threading.Tasks.Task.FromResult<global::PhotinoNetUtils.Messaging.NativeResponse?>(null)");
        builder.AppendLine($"{indent}        }};");
        builder.AppendLine($"{indent}}}");

        if (type.ContainingNamespace is { IsGlobalNamespace: false })
            builder.AppendLine("}");

        return builder.ToString();
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(syntax => syntax.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static string Literal(string value) =>
        Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true);

    private sealed class HandlerInfo(
        INamedTypeSymbol? containing,
        IMethodSymbol method,
        string messageType,
        bool returnsTask,
        List<Diagnostic> diagnostics)
    {
        public INamedTypeSymbol? Containing { get; } = containing;
        public IMethodSymbol Method { get; } = method;
        public string MessageType { get; } = messageType;
        public bool ReturnsTask { get; } = returnsTask;
        public List<Diagnostic> Diagnostics { get; } = diagnostics;
    }
}
