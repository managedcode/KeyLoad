using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record RemotePartitionQueryRf3Seed(RemoteDocumentRf3Seed Destination,
    CommandRequest LocalCommand, CommitReceipt LocalReceipt)
{
    internal const string SourceKey = "source";
    internal const string SourceJson = "{\"title\":\"source\",\"secret\":\"source-private-canary\"}";
    internal const string SourceProjected = "{\"title\":\"source\"}";
    internal const long GrantedEpoch = 2;
    internal const int Version = 1;
    internal const int RowLimit = 2;
    internal const string TitlePath = "/title";
    internal const string Tool = "keyload_query_partitions";
    internal static readonly PartitionRef Local = RemoteDocumentRf3Protocol.Partition with { PartitionKey = SourceKey };

    internal static async Task<RemotePartitionQueryRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        KeyLoadClient source, KeyLoadClient destination, bool probe, CancellationToken token)
    {
        var seed = await RemoteDocumentRf3Seed.CreateAsync(wave, source, destination, probe, token).ConfigureAwait(false);
        var principal = Authorized(seed.Principal);
        await McpCallerAssertions.SdkSuccessAsync(await source.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        var command = new CommandRequest(Guid.NewGuid(), Local,
            [new PutDocument(RemoteDocumentRf3Protocol.Collection, RemoteDocumentRf3Protocol.Document,
                SourceJson, ExpectedRevision: RemoteDocumentRf3Protocol.UnboundRevision)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await source.CommitAsync(command, token));
        return new(seed, command, receipt);
    }

    internal Task GrantAsync(KeyLoadClient destination, CancellationToken token) => GrantCoreAsync(destination, token);

    private async Task GrantCoreAsync(KeyLoadClient destination, CancellationToken token)
    {
        var expected = Authorized(Destination.Principal);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await destination.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static PrincipalRecord Authorized(PrincipalRecord principal) => principal with
    {
        PolicyEpoch = GrantedEpoch,
        Grants = [new(RemoteDocumentRf3Protocol.Database,
        RemoteDocumentRf3Protocol.Collection, Capability.Query | Capability.DocumentsRead)]
    };

    internal static PartitionQueryRequestV1 Request() => new(Version,
        [RemoteDocumentRf3Protocol.Partition, Local],
        new SelectQuery(RemoteDocumentRf3Protocol.Collection, null,
            [new(TitlePath, RemoteDocumentRf3Protocol.Title)], null, [new(TitlePath, false)], RowLimit),
        null, true, Version);
}
