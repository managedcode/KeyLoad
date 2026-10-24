using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record ModelHeadClusterState(PartitionRef Partition, CommandRequest Seed, CommitReceipt Receipt,
    McpPersistedIdentity Identity, StreamPage Original, OutboxStatus Outbox)
{
    internal const string Events = "model-head-events";
    internal const string Stream = "source";
    internal const string Queue = "model-head-queue";
    internal const string Secret = "PRIVATE-MODEL-HEAD";
    internal static readonly DateTimeOffset NotBefore = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal StreamRef Source => new(Partition, Events, Stream, 1L);
    internal QueueLaneRef Lane => new(Partition, Queue);
    internal static string Payload(string visible) => "{\"visible\":\"" + visible + "\",\"secret\":\"" + Secret + "\"}";
    internal static string Headers => "{\"trace\":\"source\",\"privateHeader\":\"" + Secret + "\"}";
}
