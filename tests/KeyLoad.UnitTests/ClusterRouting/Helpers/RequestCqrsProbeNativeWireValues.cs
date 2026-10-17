using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsProbeNativeWireValues
{
    internal const string RequestPurpose = "keyload.request-cqrs-probe-native-request.v1";
    internal const string ReplyPurpose = "keyload.request-cqrs-probe-native-reply.v1";
    internal static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    internal static RequestCqrsProbeNativeRequest Request(ReplicaSecurityFixture fixture)
    {
        var owner = new PhysicalShardRecord(Guid.NewGuid(), fixture.Configuration.Incarnation,
            fixture.Configuration.VoterIds, 1);
        var identity = new RequestCqrsProbeNativeIdentity(Guid.NewGuid(), Guid.NewGuid(), "wire-only-principal",
            null, OperationKind.BootstrapPhysicalShardCatalog, Now.AddMinutes(1), Guid.NewGuid());
        var claim = new RequestCqrsProbeNativeClaim("wire-only-session", Guid.NewGuid(), identity,
            owner, ReplicaSecurityFixture.VoterB, fixture.Discovery.RuntimeAddress,
            Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(identity))));
        return new(RequestCqrsProbeNativeProtocol.Version, RequestCqrsProbeNativeMethod.Notify, identity,
            owner, ReplicaSecurityFixture.VoterA, fixture.Discovery.RuntimeAddress, owner,
            claim.Voter, claim.SiloAddress, Guid.NewGuid(), identity.OriginalExpiresAt, claim, string.Empty);
    }
    internal static RequestCqrsProbeNativeReply Reply(RequestCqrsProbeNativeRequest request)
        => new(request.Version, request.Method, request.Nonce, request.Claim, false, null, string.Empty);
    internal static string Digest(ReadOnlySpan<byte> key, byte[] bytes, string purpose)
    {
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hash.AppendData(Encoding.ASCII.GetBytes(purpose));
        hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    internal static async Task AssertContract<T>(string alias, params string[] properties)
    {
        var type = typeof(T);
        var aliases = type.GetCustomAttributesData().Where(value => value.AttributeType == typeof(global::Orleans.AliasAttribute)).ToArray();
        await Assert.That(aliases.Length).IsEqualTo(1);
        await Assert.That((string)aliases[0].ConstructorArguments[0].Value!).IsEqualTo(alias);
        await Assert.That(type.GetCustomAttributesData().Any(value => value.AttributeType == typeof(global::Orleans.GenerateSerializerAttribute))).IsTrue();
        var fields = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        await Assert.That(fields.Length).IsEqualTo(properties.Length);
        for (var index = 0; index < properties.Length; index++)
        {
            var field = fields.Single(value => value.Name == properties[index]);
            var ids = field.GetCustomAttributesData().Where(value => value.AttributeType == typeof(global::Orleans.IdAttribute)).ToArray();
            await Assert.That(ids.Length).IsEqualTo(1);
            await Assert.That(Convert.ToUInt32(ids[0].ConstructorArguments[0].Value, global::System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo((uint)index);
        }
    }
    internal static async Task AssertExactRoundTrip<T>(T value)
    {
        var bytes = NativeSerialization.Serialize(value);
        var decoded = NativeSerialization.Deserialize<T>(bytes);
        await Assert.That(NativeSerialization.Serialize(decoded).AsSpan().SequenceEqual(bytes)).IsTrue();
    }
}
