namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Selects only an ephemeral negative ownership probe beside the original Orleans owners.</summary>
internal sealed partial class FunctionalGateFixture
{
    private readonly FunctionalGateFileContenderSettlement fileContenders;

    internal FunctionalGatePhysicalFileEvidence? PhysicalFiles
    {
        get; private set;
    }

    internal static Task<FunctionalGateFixture> StartFileOwnershipAsync(CancellationToken token)
        => StartCoreAsync(ThreeSilos, ThreeSilos, true, true, token);

    internal void RegisterFileContenderSettlement(Task<bool> settlement)
        => fileContenders.Register(settlement);
}
