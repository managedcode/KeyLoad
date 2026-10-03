using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireFlatAuthenticationTests
{
    [Test]
    public async Task AcCache014ExactFlatRejectionsCannotBeSignedOrAuthenticated()
    {
        using var authenticator = CreateAuthenticator();
        var prepare = authenticator.TrySign(
            new CachePrepareReply(null, CacheControlStatus.Rejected, null, default), out var signedPrepare);
        var grant = authenticator.TrySign(
            new CacheGrantReply(null, CacheControlStatus.Rejected, Guid.Empty, null, 0, default), out var signedGrant);
        var revoke = authenticator.TrySign(
            new CacheRevokeReply(null, CacheControlStatus.Rejected, Guid.Empty, CacheRevokeEffect.None, default), out var signedRevoke);
        var refresh = authenticator.TrySign(
            new CacheRefreshReceipt(null, CacheControlStatus.Rejected, Guid.Empty, Guid.Empty, null, default), out var signedRefresh);
        ICacheControlMessage?[] messages = [
            new CachePrepareReply(null, CacheControlStatus.Rejected, null, default),
            new CacheGrantReply(null, CacheControlStatus.Rejected, Guid.Empty, null, 0, default),
            new CacheRevokeReply(null, CacheControlStatus.Rejected, Guid.Empty, CacheRevokeEffect.None, default),
            new CacheRefreshReceipt(null, CacheControlStatus.Rejected, Guid.Empty, Guid.Empty, null, default)
        ];

        await Assert.That(new[] { prepare, grant, revoke, refresh }.Any(success => success)).IsFalse();
        await Assert.That(new object?[] { signedPrepare, signedGrant, signedRevoke, signedRefresh }
            .Any(message => message is not null)).IsFalse();
        foreach (var message in messages)
        {
            await Assert.That(authenticator.TryAuthenticate(message)).IsFalse();
        }
    }
}
