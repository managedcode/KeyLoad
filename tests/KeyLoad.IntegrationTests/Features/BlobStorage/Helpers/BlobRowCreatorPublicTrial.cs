using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRowCreatorPublicTrial
{
    internal static Task<bool> RunAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            administrator => SetupAsync(fixture, administrator, token), token);

    private static async Task<bool> SetupAsync(ClusterFixture fixture, RequestCqrsRf3Callers administrator,
        CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(administrator.Sdk, token);
        var blob = new BlobRef(scenario.Partition, RelationalSqlRf3Tokens.Blobs, "row-creator-public");
        var creatorIdentity = await BlobRowCreatorPublicPolicy.CreateAsync(fixture, administrator, blob, token);
        var otherIdentity = await BlobRowCreatorPublicPolicy.CreateAsync(fixture, administrator, blob, token);
        await Assert.That(creatorIdentity.Principal.OwnerId).IsNotEqualTo(otherIdentity.Principal.OwnerId);
        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, creatorIdentity.Secret,
            creator => WithCreatorAsync(fixture, administrator, creator, creatorIdentity, otherIdentity, blob, token), token);
    }

    private static Task<bool> WithCreatorAsync(ClusterFixture fixture, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers creator, McpPersistedIdentity creatorIdentity, McpPersistedIdentity otherIdentity,
        BlobRef blob, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, otherIdentity.Secret,
            other => ExerciseAsync(administrator, creator, other, creatorIdentity, otherIdentity, blob, token), token);

    private static async Task<bool> ExerciseAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers creator,
        RequestCqrsRf3Callers other, McpPersistedIdentity creatorIdentity, McpPersistedIdentity otherIdentity,
        BlobRef blob, CancellationToken token)
    {
        var published = await BlobRowCreatorPublicPublication.RunAsync(creator, other, creatorIdentity, otherIdentity, blob, token);
        await BlobRowCreatorPublicAssertions.ReadDeniedAsync(administrator, other, creator, otherIdentity.Secret, creatorIdentity.Secret, published, token);
        var repaired = await BlobRowCreatorPublicPolicy.PersistAsync(administrator, otherIdentity.Principal with
        { OwnerId = creatorIdentity.Principal.OwnerId, PolicyEpoch = 3L }, token);
        await BlobRowCreatorPublicAssertions.CurrentAsync(other, published, token);
        await BlobRowCreatorPublicAssertions.CreatorDeniedAsync(other, creator, otherIdentity.Secret, creatorIdentity.Secret, published, token);
        await BlobRowCreatorPublicReplay.RunAsync(creator, published, token);
        await BlobRowCreatorPublicAssertions.CurrentAsync(other, published, token);
        _ = await BlobRowCreatorPublicPolicy.PersistAsync(administrator, repaired with
        { OwnerId = otherIdentity.Principal.OwnerId, PolicyEpoch = 4L }, token);
        await BlobRowCreatorPublicAssertions.ReadDeniedAsync(administrator, other, creator, otherIdentity.Secret, creatorIdentity.Secret, published, token);
        await BlobRowCreatorPublicReplay.RunAsync(creator, published, token);
        await BlobRowCreatorPublicAssertions.ReadDeniedAsync(administrator, other, creator, otherIdentity.Secret, creatorIdentity.Secret, published, token);
        return true;
    }
}
