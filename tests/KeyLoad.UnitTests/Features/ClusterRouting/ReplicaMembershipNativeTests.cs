using System.Net;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-001: actual native membership DTOs retain CAS, ETags, suspicion and monotonic heartbeat behavior.</summary>
internal sealed class ReplicaMembershipNativeTests
{
    private const int Port = 11_111;
    private const int Generation = 1;
    private const int ProxyPort = 30_000;
    private const string Host = "native-membership-host";
    private const string Name = "native-membership-silo";
    private const string ZeroEtag = "0";
    private const string FirstEtag = "1";
    private const string SecondEtag = "2";
    private const string LegacyJson = "{\"version\":0,\"rows\":[]}";
    private const long FirstVersion = 1;
    private const long SecondVersion = 2;
    private const int OneByte = 1;
    private static readonly DateTime Started = DateTime.UnixEpoch;
    private static readonly DateTime Alive = Started.AddMinutes(OneByte);

    [Test]
    public async Task ActualSnapshotAndRowsPreserveAllNativeMembershipFields()
    {
        var entry = Entry();
        var empty = ReplicaMembershipSnapshot.Read(null);
        var inserted = empty.Insert(entry, new TableVersion((int)FirstVersion, ZeroEtag))!;
        var bytes = inserted.Serialize();
        var native = NativeSerialization.Deserialize<ReplicaMembershipTableSnapshot>(bytes);
        var restored = ReplicaMembershipSnapshot.Read(new MembershipRecord(FirstVersion, bytes));
        var row = restored.Data().Members.Single();
        await Assert.That(native.Version).IsEqualTo(FirstVersion);
        await Assert.That(native.Rows.Single().Address).IsEqualTo(entry.SiloAddress.ToParsableString());
        await Assert.That(NativeSerialization.Measure(native)).IsEqualTo((long)bytes.Length);
        await Assert.That(restored.ExpectedVersion).IsEqualTo(FirstVersion);
        await Assert.That(row.Item2).IsEqualTo(FirstEtag);
        await Assert.That(row.Item1.SiloAddress).IsEqualTo(entry.SiloAddress);
        await Assert.That(row.Item1.Status).IsEqualTo(entry.Status);
        await Assert.That(row.Item1.ProxyPort).IsEqualTo(entry.ProxyPort);
        await Assert.That(row.Item1.HostName).IsEqualTo(Host);
        await Assert.That(row.Item1.SiloName).IsEqualTo(Name);
        await Assert.That(row.Item1.StartTime).IsEqualTo(Started);
        await Assert.That(row.Item1.IAmAliveTime).IsEqualTo(Alive);
        await Assert.That(row.Item1.SuspectTimes!.Single().Item1).IsEqualTo(entry.SuspectTimes!.Single().Item1);
        await Assert.That(row.Item1.SuspectTimes!.Single().Item2).IsEqualTo(entry.SuspectTimes!.Single().Item2);
        await Assert.That(restored.Data(entry.SiloAddress).Members.Count).IsEqualTo(OneByte);
        await Assert.That(empty.Insert(entry, new TableVersion((int)FirstVersion, FirstEtag))).IsNull();
        await Assert.That(inserted.Insert(entry, new TableVersion((int)SecondVersion, FirstEtag))).IsNull();
    }

    [Test]
    public async Task HeartbeatPreservesEtagAndStaleStatusCannotMoveAliveTimeBackwards()
    {
        var entry = Entry();
        var initial = ReplicaMembershipSnapshot.Read(null).Insert(entry, new TableVersion((int)FirstVersion, ZeroEtag))!;
        var latest = Entry();
        latest.IAmAliveTime = Alive.AddMinutes(OneByte);
        var heartbeat = initial.Heartbeat(latest)!;
        await Assert.That(heartbeat.Data().Version.Version).IsEqualTo((int)FirstVersion);
        await Assert.That(heartbeat.Data().Members.Single().Item2).IsEqualTo(FirstEtag);
        await Assert.That(heartbeat.Heartbeat(entry)).IsNull();
        var updated = heartbeat.Update(entry, FirstEtag, new TableVersion((int)SecondVersion, FirstEtag))!;
        var decoded = ReplicaMembershipSnapshot.Read(new MembershipRecord(SecondVersion, updated.Serialize()));
        await Assert.That(decoded.Data().Members.Single().Item1.IAmAliveTime).IsEqualTo(latest.IAmAliveTime);
        await Assert.That(decoded.Data().Members.Single().Item2).IsEqualTo(SecondEtag);
        await Assert.That(heartbeat.Update(entry, ZeroEtag, new TableVersion((int)SecondVersion, FirstEtag))).IsNull();
        await Assert.That(heartbeat.Update(entry, FirstEtag, new TableVersion((int)FirstVersion, FirstEtag))).IsNull();
    }

    [Test]
    public async Task CleanupRemovesOnlyDeadRowsBeforeCutoffAndDeleteAdvancesVersion()
    {
        var entry = Entry();
        var alive = ReplicaMembershipSnapshot.Read(null).Insert(entry, new TableVersion((int)FirstVersion, ZeroEtag))!;
        await Assert.That(alive.Cleanup(new DateTimeOffset(Alive.AddMinutes(OneByte)))).IsNull();
        entry.Status = SiloStatus.Dead;
        var dead = alive.Update(entry, FirstEtag, new TableVersion((int)SecondVersion, FirstEtag))!;
        await Assert.That(dead.Cleanup(new DateTimeOffset(Alive))).IsNull();
        var cleaned = dead.Cleanup(new DateTimeOffset(Alive.AddMinutes(OneByte)))!;
        await Assert.That(cleaned.Data().Members.Count).IsEqualTo(0);
        await Assert.That(cleaned.Data().Version.Version).IsEqualTo((int)SecondVersion + OneByte);
        await Assert.That(dead.Delete().Data().Members.Count).IsEqualTo(0);
        await Assert.That(dead.Delete().Data().Version.Version).IsEqualTo((int)SecondVersion + OneByte);
    }

    [Test]
    public async Task LegacyJsonWrongTypeAndTrailingNativeSnapshotAreCorruptionWithoutFallback()
    {
        var native = ReplicaMembershipSnapshot.Read(null).Serialize();
        var cases = new byte[][]
        {
            Encoding.UTF8.GetBytes(LegacyJson), NativeSerialization.Serialize(LegacyJson),
            native.Append((byte)OneByte).ToArray()
        };
        foreach (var bytes in cases)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                ReplicaMembershipSnapshot.Read(new MembershipRecord(FirstVersion, bytes))).Code).IsEqualTo(ErrorCode.Corruption);
        }
    }

    internal static MembershipEntry Entry() => new()
    {
        SiloAddress = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, Port), Generation),
        Status = SiloStatus.Active,
        ProxyPort = ProxyPort,
        HostName = Host,
        SiloName = Name,
        StartTime = Started,
        IAmAliveTime = Alive,
        SuspectTimes = [Tuple.Create(SiloAddress.New(new IPEndPoint(IPAddress.Loopback, ProxyPort), Generation), Started)]
    };
}
