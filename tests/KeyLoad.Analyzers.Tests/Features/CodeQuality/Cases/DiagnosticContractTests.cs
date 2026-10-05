using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-004: verifies all sixteen diagnostic contracts with real compiler fixtures.</summary>
internal sealed class DiagnosticContractTests
{
    private const string MachineKeyId = "KLD0001";
    private const string EndpointMappingId = "KLD0013";
    private const string GrainVersionId = "KLD0014";
    private const string CompositionRootId = "KLD0020";
    private const string ContractConstructorId = "KLD0021";
    private const string SystemClockId = "KLD0022";
    private const string GenerateSerializerId = "KLD0023";
    private const string UntypedCatchId = "KLD0024";
    private const string FileCodeLineCountId = "KLD0030";
    private const string AggregateTypeCodeLineCountId = "KLD0031";
    private const string ExecutableUnitCodeLineCountId = "KLD0032";
    private const string ControlFlowNestingId = "KLD0033";
    private const string TypedSynchronizationId = "KLD0034";
    private const string MagicRuntimeDurationId = "KLD0035";
    private const string MagicRuntimeStringId = "KLD0036";
    private const string TypedConfigurationId = "KLD0037";

    /// <summary>Verifies all sixteen rules have unique enabled ids and expected severities.</summary>
    [Test]
    public async Task AllSixteenRulesHaveUniqueEnabledIdsAndExpectedSeveritiesAsync()
    {
        // AC-CQ-004: public rule inventory is stable for compiler and SARIF consumers.
        var expected = new Dictionary<string, DiagnosticSeverity>(StringComparer.Ordinal)
        {
            [MachineKeyId] = DiagnosticSeverity.Error,
            [EndpointMappingId] = DiagnosticSeverity.Error,
            [GrainVersionId] = DiagnosticSeverity.Error,
            [CompositionRootId] = DiagnosticSeverity.Error,
            [ContractConstructorId] = DiagnosticSeverity.Error,
            [SystemClockId] = DiagnosticSeverity.Error,
            [GenerateSerializerId] = DiagnosticSeverity.Error,
            [UntypedCatchId] = DiagnosticSeverity.Warning,
            [FileCodeLineCountId] = DiagnosticSeverity.Error,
            [AggregateTypeCodeLineCountId] = DiagnosticSeverity.Error,
            [ExecutableUnitCodeLineCountId] = DiagnosticSeverity.Error,
            [ControlFlowNestingId] = DiagnosticSeverity.Error,
            [TypedSynchronizationId] = DiagnosticSeverity.Error,
            [MagicRuntimeDurationId] = DiagnosticSeverity.Error,
            [MagicRuntimeStringId] = DiagnosticSeverity.Error,
            [TypedConfigurationId] = DiagnosticSeverity.Error
        };
        var analyzers = typeof(LiteralMachineKeyAnalyzer).Assembly.GetTypes()
            .Where(static type => !type.IsAbstract &&
                typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(static type => (DiagnosticAnalyzer)(Activator.CreateInstance(type) ??
                throw new InvalidOperationException($"Cannot instantiate {type.FullName}.")))
            .ToArray();
        await Assert.That(analyzers.Length).IsEqualTo(expected.Count);
        var actualIds = analyzers.SelectMany(static analyzer => analyzer.SupportedDiagnostics)
            .Select(static descriptor => descriptor.Id)
            .ToArray();
        await Assert.That(actualIds.Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(actualIds.Length);
        foreach (var analyzer in analyzers)
        {
            await Assert.That(analyzer.SupportedDiagnostics).HasSingleItem();
            var descriptor = analyzer.SupportedDiagnostics[0];
            await Assert.That(expected.ContainsKey(descriptor.Id)).IsTrue();
            await Assert.That(descriptor.DefaultSeverity).IsEqualTo(expected[descriptor.Id]);
            await Assert.That(descriptor.IsEnabledByDefault).IsTrue();
        }
    }
}
