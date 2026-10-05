using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.Features.CodeQuality;

/// <summary>Enforces native typed-options ownership for runtime configuration and operational policy.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypedConfigurationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable compiler and SARIF diagnostic identifier.</summary>
    public const string DiagnosticId = "KLD0037";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId,
        CodeQualityDiagnosticText.TypedConfigurationTitle, CodeQualityDiagnosticText.TypedConfigurationMessage,
        CodeQualityDiagnosticCategories.Architecture, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: CodeQualityDiagnosticText.TypedConfigurationDescription);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeOperation, OperationKind.Invocation,
            OperationKind.ObjectCreation, OperationKind.PropertyReference, OperationKind.SimpleAssignment);
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.Method, SymbolKind.Field, SymbolKind.Property);
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        if (!CodeQualityAssemblyNames.IsProduction(context.Compilation.AssemblyName) ||
            context.Operation.IsImplicit)
        {
            return;
        }

        if (ConfigurationReadOperations.IsRawRead(context.Compilation, context.Operation) &&
                !ConfigurationOwnership.IsWithinBinding(context.Compilation, context.ContainingSymbol) ||
            !ConfigurationOwnership.IsWithinOptions(context.Compilation, context.ContainingSymbol) &&
            IsUnownedPolicy(context))
        {
            Report(context.ReportDiagnostic, context.Operation.Syntax.GetLocation(), context.Operation.Syntax.ToString());
        }
    }

    private static bool IsUnownedPolicy(OperationAnalysisContext context) => context.Operation switch
    {
        IObjectCreationOperation creation when ConfigurationOwnership.IsOptionsType(context.Compilation, creation.Type) =>
            !ConfigurationOwnership.IsWithinBinding(context.Compilation, context.ContainingSymbol),
        IObjectCreationOperation { Constructor: { } constructor } creation when
            HardcodedDurationPolicy.IsDurationMethod(context.Compilation, constructor) ||
            OperationalPolicyBindings.IsCapacityMethod(context.Compilation, constructor, creation.Arguments) =>
            creation.Arguments.Any(argument => HardcodedDurationPolicy.IsHardcoded(context.Compilation, argument.Value, context.CancellationToken)),
        IInvocationOperation invocation when HardcodedDurationPolicy.IsDurationMethod(context.Compilation, invocation.TargetMethod) ||
            OperationalPolicyBindings.IsCapacityMethod(context.Compilation, invocation.TargetMethod, invocation.Arguments) =>
            invocation.Arguments.Any(argument => HardcodedDurationPolicy.IsHardcoded(context.Compilation, argument.Value, context.CancellationToken)),
        ISimpleAssignmentOperation { Target: IPropertyReferenceOperation property } assignment when
            OperationalPolicyBindings.IsOperationalProperty(context.Compilation, property.Property) =>
            HardcodedDurationPolicy.IsHardcoded(context.Compilation, assignment.Value, context.CancellationToken),
        _ => false
    };

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        if (!CodeQualityAssemblyNames.IsProduction(context.Compilation.AssemblyName) ||
            context.Symbol.IsImplicitlyDeclared ||
            ConfigurationOwnership.IsWithinBinding(context.Compilation, context.Symbol) ||
            ConfigurationOwnership.IsWithinOptions(context.Compilation, context.Symbol))
        {
            return;
        }

        if (context.Symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            foreach (var parameter in constructor.Parameters.Where(parameter =>
                ConfigurationOwnership.IsOptionsType(context.Compilation, parameter.Type) ||
                ConfigurationOwnership.IsConfiguration(context.Compilation, parameter.Type)))
            {
                ReportSymbol(context, parameter);
            }
        }
        else if (context.Symbol is IFieldSymbol field && ConfigurationOwnership.IsOptionsType(context.Compilation, field.Type) &&
            !OptionsSnapshotCapture.IsCaptured(context.Compilation, field, context.CancellationToken) ||
            context.Symbol is IPropertySymbol { DeclaredAccessibility: Accessibility.Public, SetMethod.DeclaredAccessibility: Accessibility.Public } property &&
            ConfigurationOwnership.IsOptionsType(context.Compilation, property.Type))
        {
            ReportSymbol(context, context.Symbol);
        }
    }

    private static void ReportSymbol(SymbolAnalysisContext context, ISymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax(context.CancellationToken);
            var location = node switch
            {
                ParameterSyntax parameter => parameter.Type?.GetLocation() ?? parameter.GetLocation(),
                VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } => declaration.Type.GetLocation(),
                PropertyDeclarationSyntax property => property.Type.GetLocation(),
                _ => node.GetLocation()
            };
            Report(context.ReportDiagnostic, location, symbol.Name);
        }
    }

    private static void Report(Action<Diagnostic> report, Location location, string subject) =>
        report(Diagnostic.Create(Rule, location, subject));
}
