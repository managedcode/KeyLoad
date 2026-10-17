using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Bridge
{
    private const int PageSize = 1;
    private const int OverflowByte = 1;
    private const string Bearer = "Bearer";
    private const string Invalid = "The authenticated native inventory response is invalid.";

    internal static async Task<ResourcePartitionInventoryRf3Reply> ReadAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        string resource, CancellationToken token)
    {
        var failures = new List<Exception>();
        HttpClient? http = null;
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        ResourcePartitionInventoryRf3Reply? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var input = new NativeResourceOwnerInventoryDiagnosticRequest(ResourcePartitionInventoryRf3Protocol.Tenant,
                ResourcePartitionInventoryRf3Protocol.Database, resource, PageSize, Guid.NewGuid());
            var expectedDirectory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(
                wave.Application, wave.Profile, token).ConfigureAwait(false);
            http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
            request = new HttpRequestMessage(HttpMethod.Post, NativeResourceOwnerInventoryDiagnosticProtocol.Path);
            request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, seed.Credential);
            request.Content = new ByteArrayContent(NativeSerialization.Serialize(input));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(NativeResourceOwnerInventoryDiagnosticProtocol.ContentType);
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            result = await ResponseAsync(response, input, seed, membership, expectedDirectory, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => response?.Dispose(), failures);
        ServerFailureObserver.Observe(() => request?.Dispose(), failures);
        ServerFailureObserver.Observe(() => http?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidDataException(Invalid);
    }

    private static async Task<ResourcePartitionInventoryRf3Reply> ResponseAsync(HttpResponseMessage response,
        NativeResourceOwnerInventoryDiagnosticRequest input, ResourcePartitionInventoryRf3Seed seed,
        RemoteTransferNativeMembership membership, PhysicalOwnerDirectoryV1 expectedDirectory, CancellationToken token)
    {
        var payload = await PayloadAsync(response, token).ConfigureAwait(false);
        await Assert.That(payload.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(seed.Credential)) < 0).IsTrue();
        await Assert.That(payload.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(ResourcePartitionInventoryRf3Protocol.PrivateMarker)) < 0).IsTrue();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return await RefusalAsync(response, payload, seed).ConfigureAwait(false);
        }
        if (response.Content.Headers.ContentType?.MediaType != NativeResourceOwnerInventoryDiagnosticProtocol.ContentType)
        {
            throw new InvalidDataException(Invalid);
        }
        ResourcePartitionInventoryRf3Signature.Verify(response, input, payload,
            membership.Owners[TwoRf3MembershipProtocol.Node1], membership.Secrets[TwoRf3MembershipProtocol.Node1]);
        var witness = NativeSerialization.Deserialize<NativeResourceOwnerInventoryDiagnosticWitness>(payload);
        await SqlRf3Protocol.EqualAsync(expectedDirectory, witness.Inventory.Control.Directory);
        await Assert.That(witness.Request).IsEqualTo(input);
        await Assert.That(NativeSerialization.Serialize(witness.Owner).AsSpan().SequenceEqual(
            NativeSerialization.Serialize(membership.Owners[TwoRf3MembershipProtocol.Node1]))).IsTrue();
        await Assert.That(witness.Inventory.Control.Request.InventoryId).IsNotEqualTo(Guid.Empty);
        await Assert.That(witness.Inventory.Control.Request.PageSize).IsEqualTo(PageSize);
        await Assert.That(witness.Inventory.Control.Request.TenantId).IsEqualTo(input.TenantId);
        await Assert.That(witness.Inventory.Control.Request.DatabaseId).IsEqualTo(input.DatabaseId);
        await Assert.That(witness.Inventory.Control.Request.ResourceName).IsEqualTo(input.ResourceName);
        await Assert.That(witness.Inventory.Control.Request.ExpectedIncarnation).IsEqualTo(witness.Inventory.Control.Lifetime.Incarnation);
        await Assert.That(witness.ReadBytes).IsGreaterThan(0L);
        await Assert.That(witness.ExaminedRecords).IsGreaterThan(0);
        return new(witness, null);
    }

    private static async Task<byte[]> PayloadAsync(HttpResponseMessage response, CancellationToken token)
    {
        var maximum = NativeResourceOwnerInventoryDiagnosticProtocol.MaximumPayloadBytes(Options.Create(new DatabaseLimits()));
        var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var payload = new byte[maximum + OverflowByte];
        var count = 0;
        while (count < payload.Length)
        {
            var read = await stream.ReadAsync(payload.AsMemory(count), token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            count += read;
        }
        if (count > maximum || response.Content.Headers.ContentLength is { } length && length != count)
        {
            throw new InvalidDataException(Invalid);
        }
        return payload.AsSpan(0, count).ToArray();
    }

    private static async Task<ResourcePartitionInventoryRf3Reply> RefusalAsync(HttpResponseMessage response,
        byte[] payload, ResourcePartitionInventoryRf3Seed seed)
    {
        await Assert.That(response.Headers.Contains(NativeResourceOwnerInventoryDiagnosticProtocol.SignatureHeader)).IsFalse();
        using var document = JsonDocument.Parse(payload);
        var codeText = document.RootElement.GetProperty(McpCallerProtocol.ProblemCode).GetString();
        if (!Enum.TryParse<ErrorCode>(codeText, out var code) || !Enum.IsDefined(code) || code.ToString() != codeText)
        {
            throw new InvalidDataException(Invalid);
        }
        await McpCallerAssertions.VerifyProblemAsync(document.RootElement, code).ConfigureAwait(false);
        await Assert.That((int)response.StatusCode).IsEqualTo(Errors.Status(code));
        var text = System.Text.Encoding.UTF8.GetString(payload);
        await Assert.That(text.Contains(seed.Credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(ResourcePartitionInventoryRf3Protocol.LateJson, StringComparison.Ordinal)).IsFalse();
        return new(null, code);
    }
}
