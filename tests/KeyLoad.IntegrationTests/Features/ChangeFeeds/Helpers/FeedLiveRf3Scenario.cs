using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Search;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal sealed record FeedLiveRf3Scenario(PartitionRef Partition, McpPersistedIdentity Identity,
    CommandRequest Original, CommitReceipt Receipt)
{
    internal EntityRef Reference => new(Partition, FeedLiveRf3Protocol.Collection, FeedLiveRf3Protocol.First);
    internal ReadChangeFeedRequest Feed => new(Partition, FeedLiveRf3Protocol.Collection,
        Limit: FeedLiveRf3Protocol.PageLimit);
    internal AstQueryRequest Query => KeyLoadQuery.From<FeedLiveOrder>(Partition,
        FeedLiveRf3Protocol.Collection, IntegrationClientOptions.Translation())
        .Where(row => row.Status == FeedLiveRf3Protocol.MatchingStatus).ToRequest(true);

    internal static async Task<FeedLiveRf3Scenario> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var partition = new PartitionRef(FeedLiveRf3Protocol.TenantPrefix + Guid.NewGuid().ToString("N"),
            FeedLiveRf3Protocol.Database, FeedLiveRf3Protocol.Domain, Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(FeedLiveRf3Protocol.Collection, ResourceKind.Collection,
            partition.TransactionDomainId)
        { FieldPolicies = [new(FeedLiveRf3Protocol.SecretField, FeedLiveRf3Protocol.SecretTag, RawReadGrant: FeedLiveRf3Protocol.SecretGrant)] };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), token));
        var principal = new PrincipalRecord(FeedLiveRf3Protocol.PrincipalPrefix + Guid.NewGuid().ToString("N"), partition.TenantId,
            [new(partition.DatabaseId, FeedLiveRf3Protocol.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.ChangesRead)], [])
        { RestrictRows = true, OwnerId = FeedLiveRf3Protocol.Owner };
        var identity = await NativeTextRf3Scenario.ConfigureIdentityAsync(fixture, principal, token);
        var original = new CommandRequest(Guid.NewGuid(), partition,
        [new PutDocument(FeedLiveRf3Protocol.Collection, FeedLiveRf3Protocol.First,
            FeedLiveRf3Protocol.FirstJson, Access: new(FeedLiveRf3Protocol.Owner)),
         new PutDocument(FeedLiveRf3Protocol.Collection, FeedLiveRf3Protocol.Hidden,
            FeedLiveRf3Protocol.FirstJson, Access: new(FeedLiveRf3Protocol.OtherOwner))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original, token));
        return new(partition, identity, original, receipt);
    }

    internal async Task<CommitReceipt> UpdateAsync(RequestCqrsRf3Callers administrator, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(new(Guid.NewGuid(), Partition,
            [new PutDocument(FeedLiveRf3Protocol.Collection, FeedLiveRf3Protocol.First,
                FeedLiveRf3Protocol.UpdatedJson, FeedLiveRf3Protocol.FirstRevision, ExplicitReplacement: true,
                Access: new(FeedLiveRf3Protocol.Owner))]), token));

    internal sealed record FeedLiveOrder(decimal Number, string Status);
}
