using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ReplicaMembershipAuthorityCapacityTests
{
    private const int RowLimit = 2;
    private const long FirstVersion = 1;
    private const long SecondVersion = 2;
    private const string InitialEtag = "0";
    private const string FirstEtag = "1";

    [Test]
    public async Task AuthorityRowBoundRejectsCompleteOverCapacitySnapshotsWithoutChangingLocalDefault()
    {
        var snapshot = ReplicaMembershipSnapshot.Read(null, UnitRoutingOptions.Membership(), RowLimit);
        var first = snapshot.Insert(Entry(11111), new TableVersion((int)FirstVersion, InitialEtag), RowLimit)!;
        var second = first.Insert(Entry(11112), new TableVersion((int)SecondVersion, FirstEtag), RowLimit)!;
        var third = Assert.ThrowsExactly<KeyLoadException>(() =>
            second.Insert(Entry(11113), new TableVersion(3, "2"), RowLimit));
        await Assert.That(third.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var bytes = second.Serialize();
        var boundedRead = ReplicaMembershipSnapshot.Read(new MembershipRecord(SecondVersion, bytes), UnitRoutingOptions.Membership(), RowLimit);
        var unboundedLocalRead = ReplicaMembershipSnapshot.Read(new MembershipRecord(SecondVersion, bytes), UnitRoutingOptions.Membership());
        await Assert.That(boundedRead.RowCount).IsEqualTo(RowLimit);
        await Assert.That(unboundedLocalRead.RowCount).IsEqualTo(RowLimit);
        var overBound = Assert.ThrowsExactly<KeyLoadException>(() =>
            ReplicaMembershipSnapshot.Read(new MembershipRecord(SecondVersion, bytes), UnitRoutingOptions.Membership(), RowLimit - 1));
        await Assert.That(overBound.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(second.Serialize().AsSpan().SequenceEqual(bytes)).IsTrue();
    }

    private static MembershipEntry Entry(int port) => new()
    {
        SiloAddress = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, port), 1),
        Status = SiloStatus.Active,
        ProxyPort = port + 1,
        HostName = "membership-bound-host",
        SiloName = "membership-bound-silo",
        StartTime = DateTime.UnixEpoch,
        IAmAliveTime = DateTime.UnixEpoch.AddMinutes(1),
        SuspectTimes = []
    };
}
