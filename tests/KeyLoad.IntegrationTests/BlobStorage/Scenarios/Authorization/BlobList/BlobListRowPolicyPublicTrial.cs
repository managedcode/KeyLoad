using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobListRowPolicyPublicTrial
{
    internal const string OwnerA = "blob-list-owner-a";
    internal const string OwnerB = "blob-list-owner-b";

    internal static Task<bool> RunAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            admin => SetupAsync(fixture, admin, token), token);

    private static async Task<bool> SetupAsync(ClusterFixture fixture, RequestCqrsClusterCallers admin, CancellationToken token)
    {
        var scenario = await RelationalSqlClusterScenario.CreateAsync(admin.Sdk, token);
        var versions = await BlobListRowPolicyPublicPublication.CreateAsync(admin, scenario.Partition, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            RelationalSqlClusterTokens.Blobs, Capability.BlobRead, token);
        var principal = await PersistAsync(admin, identity.Principal with
        {
            OwnerId = OwnerA,
            RestrictRows = true,
            PolicyEpoch = 2L
        }, token);
        return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret,
            reader => ExerciseAsync(admin, reader, principal, versions, token), token);
    }

    private static async Task<bool> ExerciseAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        PrincipalRecord principal, BlobSupersessionPublicTrial.Version[] versions, CancellationToken token)
    {
        await BlobListRowPolicyPublicAssertions.OwnerAAsync(reader, versions, token);
        var repaired = await PersistAsync(admin, principal with
        {
            OwnerId = OwnerB,
            PolicyEpoch = 3L
        }, token);
        await BlobListRowPolicyPublicAssertions.PageAsync(reader, versions, null, versions[0].Completed.Value, "a-hidden", token);
        await BlobListRowPolicyPublicAssertions.PageAsync(reader, versions, "b-visible", versions[2].Completed.Value, "c-hidden", token);
        await BlobListRowPolicyPublicAssertions.PageAsync(reader, versions, "c-hidden", null, null, token);
        var denied = await PersistAsync(admin, repaired with
        {
            Grants = [],
            PolicyEpoch = 4L
        }, token);
        await BlobListRowPolicyPublicAssertions.DeniedAsync(admin, reader, versions, token);
        _ = await PersistAsync(admin, denied with
        {
            Grants = principal.Grants,
            OwnerId = OwnerA,
            PolicyEpoch = 5L
        }, token);
        await BlobListRowPolicyPublicAssertions.OwnerAAsync(reader, versions, token);
        await BlobListRowPolicyPublicReplay.RunAsync(admin, reader, versions, token);
        return true;
    }

    private static async Task<PrincipalRecord> PersistAsync(RequestCqrsClusterCallers admin, PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        return actual;
    }
}
