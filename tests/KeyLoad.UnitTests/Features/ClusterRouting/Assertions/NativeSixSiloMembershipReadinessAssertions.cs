using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Server.Features.ClusterRouting;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeSixSiloMembershipReadinessAssertions
{
    private const int ExpectedSilos = 6;
    private const int ExpectedVersion = 1;
    private const int LengthBytes = sizeof(uint);
    private const string FingerprintDomain = "keyload.orleans.membership.active.v1";

    internal static async Task CompleteAsync(MembershipReadinessSnapshot? observed, SiloHandle[] handles)
    {
        await Assert.That(handles.Length).IsEqualTo(ExpectedSilos);
        await Assert.That(handles.Select(handle => handle.Name).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(ExpectedSilos);
        var snapshot = observed ?? throw new InvalidOperationException("The complete native six-silo view was refused.");
        await Assert.That(snapshot.Version).IsEqualTo(ExpectedVersion);
        await Assert.That(snapshot.ActiveSilos).IsEqualTo(ExpectedSilos);
        await Assert.That(snapshot.MembershipRows).IsEqualTo(ExpectedSilos);
        await Assert.That(snapshot.ActiveFingerprint).IsEqualTo(Fingerprint(handles));
    }

    internal static async Task CancelledAsync(Func<CancellationToken, MembershipReadinessSnapshot?> read)
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync().ConfigureAwait(false);
        OperationCanceledException? observed = null;
        try
        { read(caller.Token); }
        catch (OperationCanceledException error)
        { observed = error; }
        await Assert.That(observed is not null).IsTrue();
        await Assert.That(observed!.CancellationToken).IsEqualTo(caller.Token);
    }

    private static string Fingerprint(SiloHandle[] handles)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(FingerprintDomain));
        Span<byte> length = stackalloc byte[LengthBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(length, ExpectedSilos);
        hash.AppendData(length);
        foreach (var handle in handles.OrderBy(handle => handle.SiloAddress.ToParsableString(), StringComparer.Ordinal))
        {
            var address = Encoding.UTF8.GetBytes(handle.SiloAddress.ToParsableString());
            BinaryPrimitives.WriteUInt32LittleEndian(length, checked((uint)address.Length));
            hash.AppendData(length);
            hash.AppendData(address);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
