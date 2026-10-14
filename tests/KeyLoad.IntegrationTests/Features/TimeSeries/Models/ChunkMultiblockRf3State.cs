using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed class ChunkMultiblockRf3State(SampleChunkRf3Scenario scenario, McpPersistedIdentity reader)
{
    private const long FirstPurge = 128;
    private const long CompletePurge = 256;
    private const long NoPurge = 0;
    internal SampleChunkRf3Scenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal ChunkMultiblockRf3Phase Phase { get; set; } = ChunkMultiblockRf3Phase.Opened;
    internal bool Opened => Phase == ChunkMultiblockRf3Phase.Opened;
    internal bool Expired => Phase is ChunkMultiblockRf3Phase.FirstExpired or ChunkMultiblockRf3Phase.SecondExpired
        or ChunkMultiblockRf3Phase.HealthyPending or ChunkMultiblockRf3Phase.HealthyStable;
    internal bool Healthy => Phase is ChunkMultiblockRf3Phase.HealthyPending or ChunkMultiblockRf3Phase.HealthyStable;
    internal long Purged => Phase == ChunkMultiblockRf3Phase.FirstExpired ? FirstPurge : Expired ? CompletePurge : NoPurge;
    internal bool HasMore => Phase == ChunkMultiblockRf3Phase.FirstExpired;
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
