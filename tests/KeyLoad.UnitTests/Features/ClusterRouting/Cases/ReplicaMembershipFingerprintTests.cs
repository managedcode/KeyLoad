using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ReplicaMembershipFingerprintTests
{
    private static readonly string[] Addresses = NativeAddresses(1);

    [Test]
    public async Task FingerprintMatchesIndependentLengthFramedOracleAndIgnoresInputOrder()
    {
        var actual = ReplicaMembershipFingerprint.Compute(Addresses);
        var shuffled = ReplicaMembershipFingerprint.Compute(Addresses.Reverse());
        await Assert.That(actual).IsEqualTo(IndependentFingerprint(Addresses));
        await Assert.That(shuffled).IsEqualTo(actual);
    }

    [Test]
    public async Task FingerprintBindsGenerationAndRejectsDuplicateOrUnboundedMembers()
    {
        var changed = Addresses.ToArray();
        changed[2] = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 11113), 301).ToParsableString();
        await Assert.That(ReplicaMembershipFingerprint.Compute(changed)).IsNotEqualTo(
            ReplicaMembershipFingerprint.Compute(Addresses));
        var duplicate = Addresses.ToArray();
        duplicate[5] = duplicate[4];
        var duplicateFailure = Assert.ThrowsExactly<ArgumentException>(() =>
            ReplicaMembershipFingerprint.Compute(duplicate));
        await Assert.That(duplicateFailure).IsNotNull();
        var oversized = Addresses.ToArray();
        oversized[0] = new string('x', 257);
        var oversizedFailure = Assert.ThrowsExactly<ArgumentException>(() =>
            ReplicaMembershipFingerprint.Compute(oversized));
        await Assert.That(oversizedFailure).IsNotNull();
        var missingFailure = Assert.ThrowsExactly<ArgumentException>(() =>
            ReplicaMembershipFingerprint.Compute(Addresses.Take(5)));
        await Assert.That(missingFailure).IsNotNull();
    }

    [Test]
    public async Task FingerprintLengthFramingDistinguishesAmbiguousConcatenations()
    {
        var left = new[] { "a", "bc", "d", "e", "f", "g" };
        var right = new[] { "ab", "c", "d", "e", "f", "g" };
        await Assert.That(string.Concat(left.OrderBy(value => value, StringComparer.Ordinal)))
            .IsEqualTo(string.Concat(right.OrderBy(value => value, StringComparer.Ordinal)));
        await Assert.That(ReplicaMembershipFingerprint.Compute(left))
            .IsNotEqualTo(ReplicaMembershipFingerprint.Compute(right));
    }

    private static string IndependentFingerprint(IEnumerable<string> values)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("keyload.orleans.membership.active.v1"));
            var ordered = values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            WriteLength(writer, ordered.Length);
            foreach (var value in ordered)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                WriteLength(writer, bytes.Length);
                writer.Write(bytes);
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteLength(BinaryWriter writer, int length)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)length));
        writer.Write(bytes);
    }

    private static string[] NativeAddresses(int generation)
        => Enumerable.Range(0, 6).Select(index => SiloAddress.New(
            new IPEndPoint(IPAddress.Loopback, 11_111 + index), generation + index).ToParsableString()).ToArray();
}
