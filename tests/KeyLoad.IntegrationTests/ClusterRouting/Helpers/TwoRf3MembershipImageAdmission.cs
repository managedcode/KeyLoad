using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipImageAdmission
{
    internal static async Task<string?> ReadReferenceAsync(TwoRf3MembershipWave wave, string repository,
        Action<string>? observe, CancellationToken token)
    {
        observe?.Invoke("VerifiedImageRead");
        if (wave.localImageSelection is null)
        {
            return await ClusterFixtureImageIdentity.ReadVerifiedReferenceAsync(token).ConfigureAwait(false);
        }
        _ = await LocalRf3ImageIdentity.ReadVerifiedAsync(repository, wave.localImageSelection, token)
            .ConfigureAwait(false) ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch);
        return null;
    }
}
