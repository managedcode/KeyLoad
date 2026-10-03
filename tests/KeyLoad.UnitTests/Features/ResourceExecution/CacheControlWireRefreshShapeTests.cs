using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireRefreshShapeTests
{
    [Test]
    public async Task AcCache014RefreshHeaderAcceptsOnlyTheThreeDefinedSessionRoundShapes()
    {
        var hint = CacheControlWireTestData.RefreshHint();
        var emptySessionAndRound = hint;
        var sessionWithoutRound = hint with
        {
            Header = hint.Header with { CoordinatorSessionId = CacheControlWireTestData.SessionId }
        };
        var sessionAndRound = hint with
        {
            Header = hint.Header with
            {
                CoordinatorSessionId = CacheControlWireTestData.SessionId,
                RoundNonce = CacheControlWireTestData.RoundNonce
            }
        };
        var emptySessionWithRound = hint with
        {
            Header = hint.Header with { RoundNonce = CacheControlWireTestData.RoundNonce }
        };

        await Assert.That(Encode(emptySessionAndRound)).IsTrue();
        await Assert.That(Encode(sessionWithoutRound)).IsTrue();
        await Assert.That(Encode(sessionAndRound)).IsTrue();
        await Assert.That(Encode(emptySessionWithRound)).IsFalse();
    }

    private static bool Encode(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
        if (!encoded && bytes.Length != 0)
        {
            throw new InvalidOperationException(PartialOutputMessage);
        }

        return encoded && bytes.Length > 0;
    }

    private const string PartialOutputMessage = "Invalid refresh shapes must not expose transcript bytes.";
}
