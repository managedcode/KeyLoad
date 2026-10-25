namespace KeyLoad.ClusterTests.Messaging;

internal sealed class CompetingInboxPublicState(ClusterFixture fixture, bool equivalent, TimeProvider clock)
{
    internal TimeProvider Clock => clock;
    internal Dictionary<string, (DateTimeOffset Before, DateTimeOffset After)> EventWindows
    {
        get;
    } = new(StringComparer.Ordinal);
    internal Dictionary<string, NodeStatus> Statuses
    {
        get;
    } = new(StringComparer.Ordinal);
    internal ClusterFixture Fixture => fixture;
    internal bool Equivalent => equivalent;
    internal PartitionRef Partition
    {
        get;
    } = new(CompetingInboxPublicProtocol.TenantPrefix + Guid.NewGuid().ToString(CompetingInboxPublicProtocol.GuidFormat),
        CompetingInboxPublicProtocol.DatabaseName, CompetingInboxPublicProtocol.Domain, Guid.NewGuid().ToString(CompetingInboxPublicProtocol.GuidFormat));
    internal QueueLaneRef Lane => new(Partition, CompetingInboxPublicProtocol.Input);
    internal MessagingClusterIdentity Identity
    {
        get;
        set;
    } = null!;
    internal EntityRefLifetime DocumentsLifetime
    {
        get;
        set;
    } = null!;
    internal EntityRef StoredDocument(string id) => new(Partition, CompetingInboxPublicProtocol.Documents, id) { Lifetime = DocumentsLifetime };
    internal List<(CommandRequest Request, CommitReceipt Receipt)> Producers
    {
        get;
    } = [];
    internal ProcessingRequest[] Candidates
    {
        get;
        set;
    } = [];
    internal CommitReceipt Winner
    {
        get;
        set;
    } = null!;
    internal int WinnerIndex
    {
        get;
        set;
    }
    internal List<(ProcessingRequest Request, CommitReceipt Receipt)> Current
    {
        get;
    } = [];
    internal Dictionary<string, EventRecord[]> Events
    {
        get;
    } = new(StringComparer.Ordinal);
    internal Dictionary<long, OutboxStatus> Outboxes
    {
        get;
    } = [];
    internal ProcessingRequest StaleCurrent
    {
        get;
        set;
    } = null!;
    internal bool HasHealthy
    {
        get;
        set;
    }
    internal CompetingInboxPublicCold Cold
    {
        get;
    } = new(fixture);
    internal string Json(int index) => equivalent || index == CompetingInboxPublicProtocol.FirstCandidate
        ? CompetingInboxPublicProtocol.FirstJson : CompetingInboxPublicProtocol.SecondJson;
    internal static Mutation[] Effects(string id, string json)
        => [new PutDocument(CompetingInboxPublicProtocol.Documents, id, json, 0L),
            new AppendEvents(CompetingInboxPublicProtocol.Streams, id, [new(id, CompetingInboxPublicProtocol.EventType, json)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(CompetingInboxPublicProtocol.Output, id, json)];
    internal ProcessingRequest Processing(Delivery delivery, string id, string json)
        => new(Guid.NewGuid(), Lane, delivery.Token, CompetingInboxPublicProtocol.Handler,
            CompetingInboxPublicProtocol.ExecutionGeneration, [.. Effects(id, json)]);
}
