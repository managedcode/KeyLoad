using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.Search;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal sealed record FeedLiveClusterScenario(PartitionRef Partition, McpPersistedIdentity Identity,
    CommandRequest Original, CommitReceipt Receipt)
{
    internal EntityRef Reference => new(Partition, FeedLiveClusterProtocol.Collection, FeedLiveClusterProtocol.First);
    internal ReadChangeFeedRequest Feed => new(Partition, FeedLiveClusterProtocol.Collection,
        Limit: FeedLiveClusterProtocol.PageLimit);
    internal AstQueryRequest Query => KeyLoadQuery.From<FeedLiveOrder>(Partition,
        FeedLiveClusterProtocol.Collection, IntegrationClientOptions.Translation())
        .Where(row => row.Status == FeedLiveClusterProtocol.MatchingStatus).ToRequest(true);

    internal static async Task<FeedLiveClusterScenario> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var partition = new PartitionRef(FeedLiveClusterProtocol.TenantPrefix + Guid.NewGuid().ToString("N"),
            FeedLiveClusterProtocol.Database, FeedLiveClusterProtocol.Domain, Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(FeedLiveClusterProtocol.Collection, ResourceKind.Collection,
            partition.TransactionDomainId)
        {
            FieldPolicies = [new(FeedLiveClusterProtocol.SecretField, FeedLiveClusterProtocol.SecretTag, RawReadGrant: FeedLiveClusterProtocol.SecretGrant)]
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), token));
        var principal = new PrincipalRecord(FeedLiveClusterProtocol.PrincipalPrefix + Guid.NewGuid().ToString("N"), partition.TenantId,
            [new(partition.DatabaseId, FeedLiveClusterProtocol.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.ChangesRead)], [])
        {
            RestrictRows = true,
            OwnerId = FeedLiveClusterProtocol.Owner
        };
        var identity = await TextClusterScenario.ConfigureIdentityAsync(fixture, principal, token);
        var original = new CommandRequest(Guid.NewGuid(), partition,
        [new PutDocument(FeedLiveClusterProtocol.Collection, FeedLiveClusterProtocol.First,
            FeedLiveClusterProtocol.FirstJson, Access: new(FeedLiveClusterProtocol.Owner)),
         new PutDocument(FeedLiveClusterProtocol.Collection, FeedLiveClusterProtocol.Hidden,
            FeedLiveClusterProtocol.FirstJson, Access: new(FeedLiveClusterProtocol.OtherOwner))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original, token));
        return new(partition, identity, original, receipt);
    }

    internal async Task<CommitReceipt> UpdateAsync(RequestCqrsClusterCallers administrator, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(new(Guid.NewGuid(), Partition,
            [new PutDocument(FeedLiveClusterProtocol.Collection, FeedLiveClusterProtocol.First,
                FeedLiveClusterProtocol.UpdatedJson, FeedLiveClusterProtocol.FirstRevision, ExplicitReplacement: true,
                Access: new(FeedLiveClusterProtocol.Owner))]), token));

    internal sealed record FeedLiveOrder(decimal Number, string Status);
}
