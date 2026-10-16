using System.Net;
using System.Text;
using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using KeyLoad.Server.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourceRetirementRf3Response
{
    private const int OverflowByte = 1;

    internal static async Task<(NativeResourceRetirementDiagnosticWitness? Witness, ErrorCode? Error)> ReadAsync(
        HttpResponseMessage response, NativeResourceRetirementDiagnosticRequest input, ReplicaSiloDiscovery owner,
        ReadOnlyMemory<byte> secret, string credential, CancellationToken token)
    {
        var payload = await PayloadAsync(response, token).ConfigureAwait(false);
        await Assert.That(payload.AsSpan().IndexOf(Encoding.UTF8.GetBytes(credential)) < 0).IsTrue();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (null, await RefusalAsync(response, payload).ConfigureAwait(false));
        }
        if (response.Content.Headers.ContentType?.MediaType != NativeResourceRetirementDiagnosticProtocol.ContentType)
        {
            throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        }
        ResourceRetirementRf3Signature.Verify(response, input, payload, owner, secret);
        var witness = NativeSerialization.Deserialize<NativeResourceRetirementDiagnosticWitness>(payload)
            ?? throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        await Assert.That(NativeSerialization.Serialize(witness.Request).AsSpan().SequenceEqual(
            NativeSerialization.Serialize(input))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(witness.Owner).AsSpan().SequenceEqual(
            NativeSerialization.Serialize(owner))).IsTrue();
        await Assert.That(witness.Owner.TransportReady).IsTrue();
        await Assert.That(witness.ReadBytes).IsGreaterThan(0L);
        await Assert.That(witness.ExaminedRecords).IsGreaterThan(0);
        RequireShape(input, witness);
        token.ThrowIfCancellationRequested();
        return (witness, null);
    }

    private static void RequireShape(NativeResourceRetirementDiagnosticRequest input,
        NativeResourceRetirementDiagnosticWitness witness)
    {
        ResourceRetirementRequestV1 actual;
        switch (input.Operation)
        {
            case ResourceRetirementDiagnosticOperation.Execute:
            case ResourceRetirementDiagnosticOperation.ReadState:
                if (witness.State is null || witness.Preparation is not null)
                {
                    throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
                }
                actual = witness.State.Control.Request;
                break;
            case ResourceRetirementDiagnosticOperation.ReadPreparation:
                if (witness.State is not null || witness.Preparation is null)
                {
                    throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
                }
                actual = witness.Preparation.Control.Request;
                break;
            default:
                throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        }
        if (!string.Equals(JsonSerializer.Serialize(actual, JsonDefaults.Options),
            JsonSerializer.Serialize(input.Request, JsonDefaults.Options), StringComparison.Ordinal))
        {
            throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        }
    }

    private static async Task<byte[]> PayloadAsync(HttpResponseMessage response, CancellationToken token)
    {
        var maximum = NativeResourceRetirementDiagnosticProtocol.MaximumPayloadBytes(Options.Create(new DatabaseLimits()));
        if (response.Content.Headers.ContentLength is { } length && length > maximum)
        {
            throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
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
        token.ThrowIfCancellationRequested();
        if (count > maximum || response.Content.Headers.ContentLength is { } expected && expected != count)
        {
            throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        }
        return payload.AsSpan(0, count).ToArray();
    }

    private static async Task<ErrorCode> RefusalAsync(HttpResponseMessage response, byte[] payload)
    {
        await Assert.That(response.Headers.Contains(NativeResourceRetirementDiagnosticProtocol.SignatureHeader)).IsFalse();
        using var document = JsonDocument.Parse(payload);
        var codeText = document.RootElement.GetProperty(McpCallerProtocol.ProblemCode).GetString();
        if (!Enum.TryParse<ErrorCode>(codeText, out var code) || !Enum.IsDefined(code) || code.ToString() != codeText)
        {
            throw new InvalidDataException(ResourceRetirementRf3Bridge.Invalid);
        }
        await McpCallerAssertions.VerifyProblemAsync(document.RootElement, code).ConfigureAwait(false);
        await Assert.That((int)response.StatusCode).IsEqualTo(Errors.Status(code));
        return code;
    }
}
