using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Reads the existing private listener's exact typed lifetime-command scope from all six current signed owners.</summary>
internal static class NativeCommandLifetimeCutClient
{
    private const int OverflowByte = 1;
    private const int SignatureCharacters = 64;
    private const string Invalid = "The signed lifetime-command cut does not match its original owner or scope.";

    internal static async Task<Dictionary<string, NativeCommandLifetimeCutWitness>> ReadSixAsync(
        TwoRf3MembershipWave wave, string credential, Guid armId, CommandRequest command,
        Guid effectId, CancellationToken token)
    {
        var controls = wave.canonicalCutControls ?? throw new InvalidDataException(Invalid);
        controls.MirrorArm(armId);
        var owners = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var result = new Dictionary<string, NativeCommandLifetimeCutWitness>(StringComparer.Ordinal);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var owner = owners.Single(value => new Uri(value.VoterId).Host == node);
            var encoded = owner.Incarnation == wave.Profile.Incarnation ? wave.Profile.PeerSecret
                : wave.MovementTargetPeerKey ?? throw new InvalidDataException(Invalid);
            var secret = Convert.FromBase64String(encoded);
            try
            {
                var request = new NativeCommandLifetimeCutRequest(Guid.NewGuid(), armId, wave.QueryControls.SessionId,
                    command.CommandId, PartitionMovementPublicParentRf3Administrator.PrincipalId, command.Partition,
                    effectId, PartitionStoreProtocol.AdministratorId);
                NativeCommandLifetimeCutCodec.Validate(request);
                result.Add(node, await ReadOneAsync(wave, node, credential, request, owner, secret, token));
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }
        return result;
    }

    private static async Task<NativeCommandLifetimeCutWitness> ReadOneAsync(TwoRf3MembershipWave wave,
        string node, string credential, NativeCommandLifetimeCutRequest original, ReplicaSiloDiscovery owner,
        byte[] secret, CancellationToken token)
    {
        var failures = new List<Exception>();
        HttpClient? http = null;
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        NativeCommandLifetimeCutWitness? witness = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            http = McpCallerHttp.Create(wave.Application, node);
            request = new(HttpMethod.Post, NativeCommandLifetimeCutWire.Path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            request.Content = new ByteArrayContent(NativeSerialization.Serialize(original));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(NativeCanonicalModelCutDiagnosticProtocol.ContentType);
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentType?.MediaType != NativeCanonicalModelCutDiagnosticProtocol.ContentType)
            {
                throw new InvalidDataException(Invalid);
            }
            var bytes = await PayloadAsync(response, token);
            Verify(response, owner, original.Nonce, secret, bytes);
            witness = NativeCommandLifetimeCutCodec.Decode(bytes, token);
            if (witness.Request != original
                || !JsonDefaults.Serialize(witness.Owner).AsSpan().SequenceEqual(JsonDefaults.Serialize(owner)))
            {
                throw new InvalidDataException(Invalid);
            }
        }, failures);
        if (response is { } ownedResponse)
        {
            ServerFailureObserver.Observe(ownedResponse.Dispose, failures);
        }
        if (request is { } ownedRequest)
        {
            ServerFailureObserver.Observe(ownedRequest.Dispose, failures);
        }
        if (http is { } ownedHttp)
        {
            ServerFailureObserver.Observe(ownedHttp.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return witness ?? throw new InvalidDataException(Invalid);
    }

    private static async Task<byte[]> PayloadAsync(HttpResponseMessage response, CancellationToken token)
    {
        var maximum = NativeCommandLifetimeCutWire.MaximumRecordBytes;
        if (response.Content.Headers.ContentLength is { } length && length > maximum)
        {
            throw new InvalidDataException(Invalid);
        }
        var failures = new List<Exception>();
        Stream? input = null;
        var payload = new byte[maximum + OverflowByte];
        var count = 0;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            input = await response.Content.ReadAsStreamAsync(token);
            while (count < payload.Length)
            {
                var read = await input.ReadAsync(payload.AsMemory(count), token);
                if (read == 0)
                {
                    break;
                }
                count += read;
            }
            if (count > maximum || response.Content.Headers.ContentLength is { } expected && expected != count)
            {
                throw new InvalidDataException(Invalid);
            }
        }, failures);
        if (input is { } owned)
        {
            await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return payload.AsSpan(0, count).ToArray();
    }

    private static void Verify(HttpResponseMessage response, ReplicaSiloDiscovery owner, Guid nonce,
        byte[] secret, byte[] bytes)
    {
        if (!response.Headers.TryGetValues(NativeCommandLifetimeCutWire.SignatureHeader, out var values))
        {
            throw new InvalidDataException(Invalid);
        }
        using var iterator = values.GetEnumerator();
        if (!iterator.MoveNext())
        {
            throw new InvalidDataException(Invalid);
        }
        var signature = iterator.Current;
        if (iterator.MoveNext() || signature.Length != SignatureCharacters)
        {
            throw new InvalidDataException(Invalid);
        }
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature);
        }
        catch (FormatException) { throw new InvalidDataException(Invalid); }
        using var mac = new ReplicaMessageMac(secret, owner.ClusterId);
        var expected = mac.Discovery(owner.Incarnation, owner.VoterId, nonce, NativeCommandLifetimeCutWire.Frame(bytes));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            {
                throw new InvalidDataException(Invalid);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(supplied);
        }
    }
}
