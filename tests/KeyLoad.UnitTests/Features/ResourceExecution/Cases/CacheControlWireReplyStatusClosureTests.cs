using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireReplyStatusClosureTests
{
    [Test]
    public async Task AcCache014PrepareReplyRejectsEveryKnownStatusOutsideItsMethodSubset()
    {
        var correlation = Correlation(CacheControlOperation.Prepare, CacheVoterSlot.Slot1);
        CacheControlStatus[] statuses =
        [CacheControlStatus.AcceptedActive, CacheControlStatus.AcceptedCold, CacheControlStatus.StaleChallenge,
            CacheControlStatus.Revoked, CacheControlStatus.HintAcknowledged];

        foreach (var status in statuses)
        {
            await AssertRejectedAsync(new CachePrepareReply(correlation, status, null, NondefaultMac));
        }
    }

    [Test]
    public async Task AcCache014GrantReplyRejectsEveryKnownStatusOutsideItsMethodSubset()
    {
        var correlation = Correlation(CacheControlOperation.Grant, CacheVoterSlot.Slot2);
        CacheControlStatus[] statuses =
        [CacheControlStatus.Ready, CacheControlStatus.Revoked, CacheControlStatus.HintAcknowledged];

        foreach (var status in statuses)
        {
            await AssertRejectedAsync(new CacheGrantReply(correlation, status, GrantId, null, 0, NondefaultMac));
        }
    }

    [Test]
    public async Task AcCache014RevokeReplyRejectsEveryKnownStatusOutsideItsMethodSubset()
    {
        var correlation = Correlation(CacheControlOperation.Revoke, CacheVoterSlot.Slot0);
        CacheControlStatus[] statuses =
        [CacheControlStatus.Ready, CacheControlStatus.AcceptedActive, CacheControlStatus.AcceptedCold,
            CacheControlStatus.Busy, CacheControlStatus.NotReady, CacheControlStatus.HintAcknowledged];

        foreach (var status in statuses)
        {
            await AssertRejectedAsync(new CacheRevokeReply(correlation, status, GrantId, CacheRevokeEffect.None, NondefaultMac));
        }
    }

    [Test]
    public async Task AcCache014RefreshReplyRejectsEveryKnownStatusOutsideItsMethodSubset()
    {
        var correlation = Correlation(CacheControlOperation.Refresh, null);
        CacheControlStatus[] statuses =
        [CacheControlStatus.Ready, CacheControlStatus.AcceptedActive, CacheControlStatus.AcceptedCold,
            CacheControlStatus.StaleChallenge, CacheControlStatus.Revoked];

        foreach (var status in statuses)
        {
            await AssertRejectedAsync(new CacheRefreshReceipt(correlation, status, Guid.Empty, Guid.Empty, null, NondefaultMac));
        }
    }

    private static async Task AssertRejectedAsync(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
        await Assert.That(encoded).IsFalse();
        await Assert.That(bytes.Length).IsEqualTo(0);
    }

    private static CacheReplyCorrelation Correlation(CacheControlOperation operation, CacheVoterSlot? target)
    {
        var header = CacheControlWireTestData.Header(operation, CacheVoterSlot.Slot0, target);
        return new(header, CacheControlWireTestData.Digest(0x51));
    }

    private static readonly CacheControlDigest NondefaultMac = CacheControlWireTestData.NondefaultMac;
    private static readonly Guid GrantId = CacheControlWireTestData.GrantId;
}
