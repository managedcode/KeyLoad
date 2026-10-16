using System.Net.Http.Headers;
using Aspire.Hosting;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourceRetirementRf3Bridge
{
    internal const string Invalid = "The authenticated native retirement response is invalid.";
    private const string Bearer = "Bearer";

    internal static async Task<(NativeResourceRetirementDiagnosticWitness? Witness, ErrorCode? Error)> SendAsync(
        DistributedApplication app, string node, string credential, NativeResourceRetirementDiagnosticRequest input,
        ReplicaSiloDiscovery owner, ReadOnlyMemory<byte> secret, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        ArgumentNullException.ThrowIfNull(input);
        if (!owner.TransportReady || secret.Length != ReplicaTransportProtocol.SecretBytes || input.Nonce == Guid.Empty
            || !Enum.IsDefined(input.Operation))
        {
            throw new InvalidDataException(Invalid);
        }
        var bytes = NativeSerialization.Serialize(input);
        if (bytes.Length > new DatabaseLimits().MaxBatchBytes)
        {
            throw new InvalidDataException(Invalid);
        }
        var failures = new List<Exception>();
        HttpClient? http = null;
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        (NativeResourceRetirementDiagnosticWitness? Witness, ErrorCode? Error)? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            http = McpCallerHttp.Create(app, node);
            request = new HttpRequestMessage(HttpMethod.Post, NativeResourceRetirementDiagnosticProtocol.Path);
            request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, credential);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(NativeResourceRetirementDiagnosticProtocol.ContentType);
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            result = await ResourceRetirementRf3Response.ReadAsync(response, input, owner, secret,
                credential, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => response?.Dispose(), failures);
        ServerFailureObserver.Observe(() => request?.Dispose(), failures);
        ServerFailureObserver.Observe(() => http?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidDataException(Invalid);
    }
}
