using System.Collections.Immutable;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed class SharedDomainPublicState(ImmutableArray<SharedDomainScope> scopes, MessagingClusterIdentity identity)
{
    internal ImmutableArray<SharedDomainScope> Scopes { get; } = scopes;
    internal MessagingClusterIdentity Identity { get; set; } = identity;
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Originals { get; } = [];
    internal List<(CommandRequest Command, CommitReceipt Receipt)> Healthy { get; } = [];
    internal Dictionary<string, EventRecord[]> RecordedEvents { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, OutboxStatus> Outboxes { get; } = new(StringComparer.Ordinal);
    internal ProcessingRequest? Processing
    {
        get; set;
    }
    internal CommitReceipt? ProcessingReceipt
    {
        get; set;
    }
    internal Delivery? Delivery
    {
        get; set;
    }
    internal bool Processed
    {
        get; set;
    }
    internal bool HealthyWritten
    {
        get; set;
    }
    internal bool Fenced
    {
        get; set;
    }
}
