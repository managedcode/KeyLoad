namespace KeyLoad.AnalyzerTests.CodeQuality;

/// <summary>AC-CQ-034/037: owned primitive projections retain configured diagnostics budgets.</summary>
internal sealed class TypedConfigurationDiagnosticsPolicyTests
{
    [Test]
    public async Task ActualBankAndFacadeRejectConstBackedOperationalOperandsAsync()
    {
        const string source = """
            using Bank = KeyLoad.Diagnostics.ResourceExecution.DatabasePhaseBank;
            using KeyLoad.Diagnostics.ResourceExecution;
            internal static class Fields
            {
                internal const int Stripes = 4;
                internal const int CasAttempts = 8;
            }
            internal static class Subject
            {
                internal static void Initialize()
                {
                    var bank = [|new Bank(true, maximumCasAttempts: Fields.CasAttempts, stripeCount: Fields.Stripes)|];
                    [|DatabasePhaseTelemetry.Initialize(false, Fields.Stripes, Fields.CasAttempts)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task TheSameOptionsSnapshotReachesBothPrimitiveProjectionOwnersAsync()
    {
        const string source = """
            using KeyLoad.Diagnostics.ResourceExecution;
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy
            {
                public int Stripes { get; init; } = 4;
                public int CasAttempts { get; init; } = 8;
            }
            internal static class Subject
            {
                internal static void Initialize(Microsoft.Extensions.Options.IOptions<Policy> configured)
                {
                    var snapshot = configured.Value;
                    var bank = new DatabasePhaseBank(true, snapshot.Stripes, snapshot.CasAttempts);
                    DatabasePhaseTelemetry.Initialize(false, snapshot.Stripes, snapshot.CasAttempts);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    [Arguments(AnalyzerFixture.ServerAssembly)]
    [Arguments("KeyLoad.Diagnostics")]
    public async Task ApplicationSourceNamesCannotBecomeOwnedPolicyApisAsync(string assemblyName)
    {
        const string source = """
            namespace KeyLoad.Diagnostics.ResourceExecution
            {
                internal sealed class DatabasePhaseBank(bool enabled, int stripeCount, int maximumCasAttempts) { }
                internal static class DatabasePhaseTelemetry
                {
                    internal static void Initialize(bool enabled, int stripeCount, int maximumCasAttempts) { }
                }
            }
            internal static class Subject
            {
                private const int Stripes = 4;
                private const int CasAttempts = 8;
                internal static void Initialize()
                {
                    var bank = new KeyLoad.Diagnostics.ResourceExecution.DatabasePhaseBank(true, Stripes, CasAttempts);
                    KeyLoad.Diagnostics.ResourceExecution.DatabasePhaseTelemetry.Initialize(false, Stripes, CasAttempts);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source, assemblyName);
    }
}
