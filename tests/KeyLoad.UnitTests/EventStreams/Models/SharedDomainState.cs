using System.Collections.Immutable;
using KeyLoad.RecoveryHost.Features.EventStreams;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class SharedDomainState
{
    internal ImmutableArray<SharedDomainScope> Scopes { get; } = SharedDomainCorpus.Create(SharedDomainProtocol.Tenant, SharedDomainProtocol.Database);
    internal List<SharedDomainHistory> History { get; } = [];
    internal Dictionary<string, DateTimeOffset> Recorded { get; } = [];
    internal Dictionary<string, CommitReceipt> Producers { get; } = [];
    internal HashSet<string> Healthy { get; } = [];
    internal bool Processed
    {
        get; set;
    }
    internal long PolicyEpoch { get; set; } = SharedDomainProtocol.FirstRevision;
    internal bool Denied
    {
        get; set;
    }
}
