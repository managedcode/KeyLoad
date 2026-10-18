using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdProvision
{
    internal static async Task<FunctionalGateColdState> CreateAsync(RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var partition = new PartitionRef("functional-cold-" + suffix, "models", "models", suffix);
        var anchor = new EntityRef(partition, FunctionalGateColdProtocol.Resource("docs", 0), "anchor");
        foreach (var definition in Definitions(partition))
        {
            var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
            var id = Guid.NewGuid();
            await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ConfigureResourceAsync(id, request, token)));
        }
        var read = Capability.DocumentsRead | Capability.Query | Capability.EventsRead | Capability.QueueInspect
            | Capability.GraphRead | Capability.SeriesRead | Capability.VectorSearch | Capability.VectorRaw
            | Capability.TopicsRead | Capability.BlobRead;
        var write = read | Capability.DocumentsWrite | Capability.EventsAppend | Capability.QueuePublish
            | Capability.GraphWrite | Capability.SeriesAppend | Capability.TopicsPublish | Capability.BlobWrite;
        var writer = await IdentityAsync(administrator, partition, write, false, token);
        var reader = await IdentityAsync(administrator, partition, read, false, token);
        var queueOperator = await IdentityAsync(administrator, partition,
            Capability.QueueInspect | Capability.DeadLettersRead | Capability.DeadLettersRedrive, true, token);
        var state = new FunctionalGateColdState(partition, anchor, writer, reader, queueOperator, [], [])
        {
            AdditionalIdentities = await FunctionalGateColdWorkloadProvision.CreateAsync(administrator, partition, read, token)
        };
        return await FunctionalGateAnalyticsProvision.RunAsync(administrator, state, token);
    }

    internal static async Task<McpPersistedIdentity> IdentityAsync(RequestCqrsClusterCallers administrator,
        PartitionRef partition, Capability capabilities, bool queueOperator, CancellationToken token)
    {
        var identity = await McpPersistedIdentity.CreateAsync(administrator.Sdk, partition,
            FunctionalGateColdProtocol.Resource("docs", 0), Capability.None, token);
        var grants = Definitions(partition).Where(item => !queueOperator || item.Kind == ResourceKind.WorkQueue)
            .Select(item => new ScopeGrant(partition.DatabaseId, item.Name, capabilities)).ToArray();
        // Tables are created through real T-SQL after identity provisioning; grant their known exact names now.
        if (!queueOperator)
        {
            grants = [.. grants, .. Enumerable.Range(0, FunctionalGateColdProtocol.Resources)
                .Select(index => new ScopeGrant(partition.DatabaseId, FunctionalGateColdProtocol.Resource("table", index), capabilities))];
        }
        var expected = identity.Principal with
        {
            PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1),
            ClusterAdministrator = queueOperator,
            Grants = [.. grants]
        };
        var observed = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, observed);
        return identity with
        {
            Principal = observed
        };
    }

    private static IEnumerable<ResourceDefinition> Definitions(PartitionRef partition)
    {
        for (var index = 0; index < FunctionalGateColdProtocol.Resources; index++)
        {
            foreach (var (model, kind) in new[] { ("docs", ResourceKind.Collection), ("events", ResourceKind.StreamSet),
                ("queue", ResourceKind.WorkQueue), ("topic", ResourceKind.Topic), ("graph", ResourceKind.Graph),
                ("series", ResourceKind.TimeSeries), ("blobs", ResourceKind.BlobStore), ("text", ResourceKind.Collection) })
            {
                yield return new(FunctionalGateColdProtocol.Resource(model, index), kind, partition.TransactionDomainId);
            }
            yield return new(FunctionalGateColdProtocol.Resource("vectors", index), ResourceKind.Collection, partition.TransactionDomainId)
            {
                VectorProfiles = [new("/embedding", FunctionalGateSearch.Space)]
            };
        }
    }
}
