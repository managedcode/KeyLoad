using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.ClusterRouting.ControlledRetireAspireProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspireCutClient
{
    internal static async Task<Dictionary<string, NativeRetireCutWitness>> ReadAsync(TwoRf3MembershipWave wave,
        Guid armId, Guid commandId, CancellationToken token, CommitToken? receiptToken = null)
    {
        var controls = wave.canonicalCutControls ?? throw new InvalidOperationException(P.Missing);
        controls.MirrorArm(armId);
        var owners = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var result = new Dictionary<string, NativeRetireCutWitness>(StringComparer.Ordinal);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var owner = owners.Single(value => new Uri(value.VoterId).Host == node);
            var encoded = owner.Incarnation == wave.Profile.Incarnation ? wave.Profile.PeerSecret
                : wave.MovementTargetPeerKey ?? throw new InvalidOperationException(P.Missing);
            var secret = Convert.FromBase64String(encoded);
            try
            {
                var request = new NativeRetireCutRequest(Guid.NewGuid(), armId, wave.QueryControls.SessionId,
                    commandId, PartitionMovementPublicParentRf3Administrator.PrincipalId, receiptToken);
                result.Add(node, await ReadOneAsync(wave, node, request, owner, secret, token));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }
        return result;
    }

    private static async Task<NativeRetireCutWitness> ReadOneAsync(TwoRf3MembershipWave wave, string node,
        NativeRetireCutRequest original, ReplicaSiloDiscovery owner, byte[] secret, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, node);
        using var request = new HttpRequestMessage(HttpMethod.Post, NativeRetireCutWire.Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wave.Profile.AdminKey);
        request.Content = new ByteArrayContent(NativeSerialization.Serialize(original));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(NativeCanonicalModelCutDiagnosticProtocol.ContentType);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode != HttpStatusCode.OK
            || response.Content.Headers.ContentType?.MediaType != NativeCanonicalModelCutDiagnosticProtocol.ContentType)
        {
            throw new InvalidDataException(P.InvalidCut);
        }
        var bytes = await PayloadAsync(response, token);
        Verify(response, owner, original.Nonce, secret, bytes);
        var actual = NativeSerialization.Deserialize<NativeRetireCutWitness>(bytes);
        if (actual.Request != original || actual.Owner != owner || actual.Rows.IsDefault
            || !actual.Identity.SigningKey.IsEmpty || actual.Identity.Incarnation != owner.Incarnation
            || actual.Rows.Length > IntegrationExecutionOptions.DatabaseLimits().Value.MaxScanRecords)
        {
            throw new InvalidDataException(P.InvalidCut);
        }
        for (var index = P.Step; index < actual.Rows.Length; index++)
        {
            if (actual.Rows[index - P.Step].Key.Span.SequenceCompareTo(actual.Rows[index].Key.Span) >= P.First)
            {
                throw new InvalidDataException(P.InvalidCut);
            }
        }
        return actual;
    }

    private static async Task<byte[]> PayloadAsync(HttpResponseMessage response, CancellationToken token)
    {
        var maximum = IntegrationExecutionOptions.DatabaseLimits().Value.MaxQueryReadBytes;
        if (response.Content.Headers.ContentLength is { } length && length > maximum)
        {
            throw new InvalidDataException(P.InvalidCut);
        }
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[P.BufferBytes];
        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read == P.First)
            {
                break;
            }
            if (read > maximum - output.Length)
            {
                throw new InvalidDataException(P.InvalidCut);
            }
            await output.WriteAsync(buffer.AsMemory(P.First, read), token);
        }
        if (response.Content.Headers.ContentLength is { } expected && expected != output.Length)
        {
            throw new InvalidDataException(P.InvalidCut);
        }
        return output.ToArray();
    }

    private static void Verify(HttpResponseMessage response, ReplicaSiloDiscovery owner,
        Guid nonce, byte[] secret, byte[] bytes)
    {
        if (!response.Headers.TryGetValues(NativeRetireCutWire.SignatureHeader, out var values))
        {
            throw new InvalidDataException(P.InvalidCut);
        }
        var signatures = values.Take(P.Step + P.Step).ToArray();
        if (signatures.Length != P.Step || signatures[P.First].Length != P.SignatureCharacters)
        {
            throw new InvalidDataException(P.InvalidCut);
        }
        var supplied = Convert.FromHexString(signatures[P.First]);
        using var mac = new ReplicaMessageMac(secret, owner.ClusterId);
        var expected = mac.Discovery(owner.Incarnation, owner.VoterId, nonce, NativeRetireCutWire.SignatureFrame(bytes));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            {
                throw new InvalidDataException(P.InvalidCut);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(supplied);
        }
    }
}
