using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KeyLoad.Analyzers.CodeQuality;

/// <summary>Requires ordinary awaits in Orleans grain execution code.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrleansConfigureAwaitAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Stable compiler and SARIF diagnostic identifier.</summary>
    public const string DiagnosticId = "KLD0043";
    private const string ClusteringAssembly = "KeyLoad.Clustering";

    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId,
        "Use ordinary awaits in Orleans execution code",
        "Remove ConfigureAwait and use the default Orleans scheduling behavior",
        CodeQualityDiagnosticCategories.Reliability, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new System.ArgumentNullException(nameof(context));
        }
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static startContext =>
        {
            var compilation = startContext.Compilation;
            var owners = ResolveTypes(compilation, OrleansMetadataNames.GrainInterface,
                OrleansMetadataNames.GrainBaseInterface, OrleansMetadataNames.GrainServiceInterface);
            var isClustering = compilation.AssemblyName == ClusteringAssembly;
            if (!isClustering && owners.IsEmpty)
            {
                return;
            }

            var awaitables = ResolveTypes(compilation, "System.Threading.Tasks.Task", "System.Threading.Tasks.Task`1",
                "System.Threading.Tasks.ValueTask", "System.Threading.Tasks.ValueTask`1",
                "System.Threading.Tasks.TaskAsyncEnumerableExtensions",
                "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1");
            startContext.RegisterOperationAction(operation => Analyze(operation, isClustering, owners, awaitables),
                Microsoft.CodeAnalysis.OperationKind.Invocation, Microsoft.CodeAnalysis.OperationKind.MethodReference);
        });
    }

    private static ImmutableArray<INamedTypeSymbol> ResolveTypes(Compilation compilation, params string[] names) => names
        .Select(compilation.GetTypeByMetadataName).OfType<INamedTypeSymbol>()
        .Where(static type => type.DeclaringSyntaxReferences.IsEmpty).ToImmutableArray();

    private static void Analyze(OperationAnalysisContext context, bool isClustering,
        ImmutableArray<INamedTypeSymbol> owners, ImmutableArray<INamedTypeSymbol> awaitables)
    {
        var method = context.Operation is IInvocationOperation invocation
            ? invocation.TargetMethod
            : ((IMethodReferenceOperation)context.Operation).Method;
        if (method.Name == "ConfigureAwait" && awaitables.Contains(method.ContainingType.OriginalDefinition, SymbolEqualityComparer.Default)
            && (isClustering || IsOrleansOwner(context, owners)))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation()));
        }
    }

    private static bool IsOrleansOwner(OperationAnalysisContext context, ImmutableArray<INamedTypeSymbol> owners)
    {
        for (var type = context.ContainingSymbol.ContainingType; type is not null; type = type.ContainingType)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (type.AllInterfaces.Any(contract => owners.Contains(contract, SymbolEqualityComparer.Default)))
            {
                return true;
            }
        }
        return false;
    }
}
