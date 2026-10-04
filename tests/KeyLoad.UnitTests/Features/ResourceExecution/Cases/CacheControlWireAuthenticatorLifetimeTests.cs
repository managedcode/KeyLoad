using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireAuthenticatorLifetimeTests
{
    [Test]
    public async Task AcCache014AllTypedSignOverloadsClearOutputForNullInput()
    {
        using var authenticator = CreateAuthenticator();
        var proof = authenticator.TrySign((CacheReadyProof?)null, out var signedProof);
        var prepare = authenticator.TrySign((CachePrepareRequest?)null, out var signedPrepare);
        var grant = authenticator.TrySign((CacheGrantRequest?)null, out var signedGrant);
        var revoke = authenticator.TrySign((CacheRevokeRequest?)null, out var signedRevoke);
        var refresh = authenticator.TrySign((CacheRefreshHint?)null, out var signedRefresh);
        var prepareReply = authenticator.TrySign((CachePrepareReply?)null, out var signedPrepareReply);
        var grantReply = authenticator.TrySign((CacheGrantReply?)null, out var signedGrantReply);
        var revokeReply = authenticator.TrySign((CacheRevokeReply?)null, out var signedRevokeReply);
        var refreshReply = authenticator.TrySign((CacheRefreshReceipt?)null, out var signedRefreshReply);

        await Assert.That(new[] { proof, prepare, grant, revoke, refresh, prepareReply, grantReply, revokeReply, refreshReply }
            .All(success => !success)).IsTrue();
        await Assert.That(new object?[] { signedProof, signedPrepare, signedGrant, signedRevoke, signedRefresh,
            signedPrepareReply, signedGrantReply, signedRevokeReply, signedRefreshReply }.All(value => value is null)).IsTrue();
    }

    [Test]
    public async Task AcCache014EveryTypedSignOverloadThrowsAfterDisposal()
    {
        using var authenticator = CreateAuthenticator();
        authenticator.Dispose();
        Action[] calls =
        [
            () => authenticator.TrySign((CacheReadyProof?)null, out _),
            () => authenticator.TrySign((CachePrepareRequest?)null, out _),
            () => authenticator.TrySign((CacheGrantRequest?)null, out _),
            () => authenticator.TrySign((CacheRevokeRequest?)null, out _),
            () => authenticator.TrySign((CacheRefreshHint?)null, out _),
            () => authenticator.TrySign((CachePrepareReply?)null, out _),
            () => authenticator.TrySign((CacheGrantReply?)null, out _),
            () => authenticator.TrySign((CacheRevokeReply?)null, out _),
            () => authenticator.TrySign((CacheRefreshReceipt?)null, out _)
        ];
        var failures = calls.Select(Assert.ThrowsExactly<ObjectDisposedException>).ToArray();

        await Assert.That(failures.Length).IsEqualTo(TypedRootMessageCount);
        await Assert.That(failures.All(error => error.GetType() == typeof(ObjectDisposedException))).IsTrue();
    }

    [Test]
    public async Task AcCache014RealConcurrentSigningAndDisposalOwnsEveryOriginalTask()
    {
        var authenticator = CreateAuthenticator();
        var proof = CreateProofBeforeTransfer(authenticator);
        // RunAsync owns disposal, including deferred cleanup after a failed finite drain.
        var result = await CacheControlWireConcurrencySupport.RunAsync(authenticator, proof);

        await Assert.That(result.Signed).IsGreaterThan(0);
        await Assert.That(result.Closed).IsGreaterThan(0);
        await AssertDisposedInputsThrowAsync(authenticator, proof);
    }

    private static CacheReadyProof CreateProofBeforeTransfer(CacheControlAuthenticator authenticator)
    {
        try
        {
            return SignProof(authenticator, CacheVoterSlot.Slot0, CacheVoterSlot.Slot0);
        }
        catch (Exception failure)
        {
            try
            {
                authenticator.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(ProofFailureMessage, failure, cleanup);
            }

            throw;
        }
    }

    private static async Task AssertDisposedInputsThrowAsync(CacheControlAuthenticator authenticator, CacheReadyProof proof)
    {
        var closed = Assert.ThrowsExactly<ObjectDisposedException>(() => authenticator.TryAuthenticate(proof));
        var nullClosed = Assert.ThrowsExactly<ObjectDisposedException>(() => authenticator.TryAuthenticate(null));
        var invalidClosed = Assert.ThrowsExactly<ObjectDisposedException>(() => authenticator.TryAuthenticate(
            new CachePrepareReply(null, CacheControlStatus.Rejected, null, default)));
        await Assert.That(closed.GetType()).IsEqualTo(typeof(ObjectDisposedException));
        await Assert.That(nullClosed.GetType()).IsEqualTo(typeof(ObjectDisposedException));
        await Assert.That(invalidClosed.GetType()).IsEqualTo(typeof(ObjectDisposedException));
    }

    private const int TypedRootMessageCount = 9;
    private const string ProofFailureMessage = "Proof creation and its owned signer cleanup failed.";
}
