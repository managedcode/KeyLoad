using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireReplyAuthenticationTests
{
    [Test]
    public async Task AcCache014ReplyAuthenticationBindsTheCompleteRequestCorrelation()
    {
        using var authenticator = CreateAuthenticator();
        var request = SignPrepare(authenticator, CacheControlWireTestData.PrepareRequest());
        var reply = SignPrepareReply(authenticator, request,
            SignProof(authenticator, CacheVoterSlot.Slot0, CacheVoterSlot.Slot1));
        var correlation = reply.Correlation!;
        var changedHeader = reply with
        {
            Correlation = correlation with
            {
                Header = correlation.Header with
                {
                    SentUnixMilliseconds = correlation.Header.SentUnixMilliseconds + 1
                }
            }
        };
        var changedDigest = reply with
        {
            Correlation = correlation with
            {
                SignedRequestDigest = CacheControlWireTestData.Digest(0x9A)
            }
        };

        await Assert.That(authenticator.TryAuthenticate(reply)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(changedHeader)).IsFalse();
        await Assert.That(authenticator.TryAuthenticate(changedDigest)).IsFalse();
    }
}
