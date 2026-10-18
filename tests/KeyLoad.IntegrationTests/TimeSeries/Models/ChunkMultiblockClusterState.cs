using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed class ChunkMultiblockClusterState(SampleChunkClusterScenario scenario, McpPersistedIdentity reader)
{
    private const long FirstPurge = 128;
    private const long CompletePurge = 256;
    private const long NoPurge = 0;
    internal SampleChunkClusterScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal ChunkMultiblockClusterPhase Phase { get; set; } = ChunkMultiblockClusterPhase.Opened;
    internal bool Opened => Phase == ChunkMultiblockClusterPhase.Opened;
    internal bool Expired => Phase is ChunkMultiblockClusterPhase.FirstExpired or ChunkMultiblockClusterPhase.SecondExpired
        or ChunkMultiblockClusterPhase.HealthyPending or ChunkMultiblockClusterPhase.HealthyStable;
    internal bool Healthy => Phase is ChunkMultiblockClusterPhase.HealthyPending or ChunkMultiblockClusterPhase.HealthyStable;
    internal long Purged => Phase == ChunkMultiblockClusterPhase.FirstExpired ? FirstPurge : Expired ? CompletePurge : NoPurge;
    internal bool HasMore => Phase == ChunkMultiblockClusterPhase.FirstExpired;
    internal OutboxStatus? Outbox
    {
        get; set;
    }
    internal bool MaintenanceOutboxAdvanced
    {
        get; set;
    }
    internal CommandRequest? Conflict
    {
        get; set;
    }
}
