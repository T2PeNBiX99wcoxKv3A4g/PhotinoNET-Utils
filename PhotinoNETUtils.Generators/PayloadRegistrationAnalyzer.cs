using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace PhotinoNetUtils.Generators;

/// <summary>
///     Reports payload types that cross the native bridge without being registered in the app's
///     <c>JsonSerializerContext</c>. A trimmed build turns reflection serialization off, so such a
///     type only fails at run time; this analyzer fails it at build time instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PayloadRegistrationAnalyzer : DiagnosticAnalyzer
{
    private const string NativeContext = "PhotinoNetUtils.Messaging.NativeContext";
    private const string NativeBridge = "PhotinoNetUtils.Messaging.NativeBridge";
    private const string NativeResponse = "PhotinoNetUtils.Messaging.NativeResponse";
    private const string WebViewEvents = "PhotinoNetUtils.Hosting.WebViewEvents";
    private const string JsonContext = "System.Text.Json.Serialization.JsonSerializerContext";
    private const string JsonSerializable = "System.Text.Json.Serialization.JsonSerializableAttribute";

    private static readonly DiagnosticDescriptor NotRegistered = new(
        "PNU2001",
        "Native payload is not registered",
        "'{0}' crosses the native bridge but is not registered in a JsonSerializerContext; add [JsonSerializable(typeof({0}))]",
        "PhotinoNetUtils.Messaging",
        DiagnosticSeverity.Warning,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [NotRegistered];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var nativeContext = start.Compilation.GetTypeByMetadataName(NativeContext);
            if (nativeContext is null)
                return;

            var registered = Registered(start.Compilation);
            if (registered.Count == 0)
                return;

            start.RegisterOperationAction(operation => Analyze(operation, registered),
                OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, HashSet<string> registered)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var containing = method.ContainingType?.ToDisplayString();

        if (containing is null)
            return;

        if (containing == NativeContext && method.Name == "Payload" && method.TypeArguments.Length == 1)
        {
            Report(context, method.TypeArguments[0], invocation.Syntax.GetLocation(), registered);
            return;
        }

        var carriesData =
            (containing == NativeContext && method.Name == "Reply") ||
            (containing == NativeBridge && method.Name == "SerializeEvent") ||
            (containing == WebViewEvents && method.Name == "Notify") ||
            (containing == NativeResponse && method.Name == "Success");

        if (!carriesData)
            return;

        var argument = invocation.Arguments
            .FirstOrDefault(item => item.Parameter?.Name == "data")
            ?.Value;

        if (argument is not null && Unwrap(argument).Type is { } type)
            Report(context, type, argument.Syntax.GetLocation(), registered);
    }

    /// <summary>Drops the implicit conversion to the <c>object</c> parameter, so the real type shows.</summary>
    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;

        return operation;
    }

    private static void Report(OperationAnalysisContext context, ITypeSymbol type, Location location,
        HashSet<string> registered)
    {
        if (IsBuiltIn(type) || registered.Contains(Key(type)))
            return;

        context.ReportDiagnostic(Diagnostic.Create(NotRegistered, location, Key(type)));
    }

    /// <summary>Every type the app's contexts generate metadata for, including nested property types.</summary>
    private static HashSet<string> Registered(Compilation compilation)
    {
        var jsonContext = compilation.GetTypeByMetadataName(JsonContext);
        var registered = new HashSet<string>(StringComparer.Ordinal);

        if (jsonContext is null)
            return registered;

        foreach (var symbol in compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type))
        {
            if (symbol is not INamedTypeSymbol type || !Derives(type, jsonContext))
                continue;

            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != JsonSerializable)
                    continue;

                foreach (var argument in attribute.ConstructorArguments)
                    if (argument.Kind == TypedConstantKind.Type && argument.Value is ITypeSymbol registeredType)
                        Add(registeredType, registered, new HashSet<string>(StringComparer.Ordinal));
            }
        }

        return registered;
    }

    /// <summary>
    ///     Adds a registered type and everything its metadata pulls in: property types (which the
    ///     serializer always generates) and generic arguments, but not collections, because an array
    ///     or list needs its own <c>[JsonSerializable]</c> entry.
    /// </summary>
    private static void Add(ITypeSymbol type, HashSet<string> registered, HashSet<string> visited)
    {
        var key = Key(type);
        if (!visited.Add(key))
            return;

        registered.Add(key);

        switch (type)
        {
            case IArrayTypeSymbol { ElementType: { } element }:
                Add(element, registered, visited);
                return;
            case INamedTypeSymbol named:
                foreach (var argument in named.TypeArguments)
                    Add(argument, registered, visited);

                // Only source types can be inspected; BCL types are already known to the serializer.
                if (named.DeclaringSyntaxReferences.Length == 0)
                    return;

                foreach (var member in named.GetMembers())
                {
                    var memberType = member switch
                    {
                        IPropertySymbol { IsStatic: false, DeclaredAccessibility: Accessibility.Public } property =>
                            property.Type,
                        IFieldSymbol { IsStatic: false, DeclaredAccessibility: Accessibility.Public } field =>
                            field.Type,
                        _ => null
                    };

                    if (memberType is not null)
                        Add(memberType, registered, visited);
                }

                return;
        }
    }

    private static bool Derives(INamedTypeSymbol type, INamedTypeSymbol jsonContext)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, jsonContext))
                return true;

        return false;
    }

    private static bool IsBuiltIn(ITypeSymbol type) =>
        type.SpecialType != SpecialType.None ||
        type.IsAnonymousType ||
        type.TypeKind == TypeKind.Enum ||
        type is INamedTypeSymbol { Name: "JsonElement" or "JsonDocument" or "JsonNode" or "JsonObject" };

    private static string Key(ITypeSymbol type)
    {
        var name = type.WithNullableAnnotation(NullableAnnotation.None)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring("global::".Length) : name;
    }
}
