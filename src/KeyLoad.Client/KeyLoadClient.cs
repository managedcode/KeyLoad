using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>HTTP SDK. Writes keep caller command IDs; a timeout is an unknown outcome, never an automatic new write.</summary>
public sealed class KeyLoadClient(HttpClient http, string apiKey)
{
    private async Task<Result<T>> Send<T>(string path, object? request, bool write, Guid? id, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(request is null ? HttpMethod.Get : HttpMethod.Post, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (id is { } command) message.Headers.Add("X-KeyLoad-Command-Id", command.ToString());
        if (request is not null) message.Content = JsonContent.Create(request, request.GetType(), options: JsonDefaults.Options);
        try
        {
            using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var problem = await response.Content.ReadFromJsonAsync<Problem>(JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
                return problem ?? Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost, "The server response is unavailable.");
            }
            var value = await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            return Result<T>.Succeed(value!);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            return Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : cancellationToken.IsCancellationRequested ? ErrorCode.Cancelled : ErrorCode.OwnershipLost,
                write ? "The write response is unavailable. Retry the same command ID." : "The read response is unavailable.");
        }
    }
    public Task<Result<CommitReceipt>> CommitAsync(CommandRequest command, CancellationToken cancellationToken = default)
        => Send<CommitReceipt>("/v1/commands", command, true, command.CommandId, cancellationToken);
    public Task<Result<DocumentResult?>> GetAsync(EntityRef reference, CancellationToken cancellationToken = default)
        => Send<DocumentResult?>("/v1/documents/get", new GetDocumentRequest(reference), false, null, cancellationToken);
    public Task<Result<StreamPage>> ReadStreamAsync(ReadStreamRequest request, CancellationToken cancellationToken = default)
        => Send<StreamPage>("/v1/streams/read", request, false, null, cancellationToken);
    public Task<Result<ReceiveResult>> ReceiveAsync(ReceiveRequest request, CancellationToken cancellationToken = default)
        => Send<ReceiveResult>("/v1/queues/receive", request, true, request.RequestId, cancellationToken);
    public Task<Result<CommitReceipt>> CompleteAsync(DeliveryCommand request, CancellationToken cancellationToken = default)
        => Send<CommitReceipt>("/v1/queues/delivery", request, true, request.CommandId, cancellationToken);
    public Task<Result<CommitReceipt>> CommitProcessingAsync(ProcessingRequest request, CancellationToken cancellationToken = default)
        => Send<CommitReceipt>("/v1/queues/process", request, true, request.CommandId, cancellationToken);
    public Task<Result<MessageInspection?>> InspectAsync(InspectMessageRequest request, CancellationToken cancellationToken = default)
        => Send<MessageInspection?>("/v1/queues/inspect", request, false, null, cancellationToken);
    public Task<Result<QueryPage>> QueryAsync(QueryRequest request, CancellationToken cancellationToken = default)
        => Send<QueryPage>("/v1/query", request, false, null, cancellationToken);
    public Task<Result<RankedDocument[]>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        => Send<RankedDocument[]>("/v1/search", request, false, null, cancellationToken);
    public Task<Result<GraphTraversal>> TraverseAsync(TraverseRequest request, CancellationToken cancellationToken = default)
        => Send<GraphTraversal>("/v1/graph/traverse", request, false, null, cancellationToken);
    public Task<Result<SampleRecord[]>> ReadSamplesAsync(ReadSamplesRequest request, CancellationToken cancellationToken = default)
        => Send<SampleRecord[]>("/v1/series/read", request, false, null, cancellationToken);
    public Task<Result<ResourceDefinition>> ConfigureResourceAsync(Guid commandId, ConfigureResourceRequest request, CancellationToken cancellationToken = default)
        => Send<ResourceDefinition>("/v1/admin/resources", request, true, commandId, cancellationToken);
    public Task<Result<PrincipalRecord>> ConfigurePrincipalAsync(Guid commandId, PrincipalRecord principal, CancellationToken cancellationToken = default)
        => Send<PrincipalRecord>("/v1/admin/principals", new ConfigurePrincipalRequest(principal), true, commandId, cancellationToken);
    public Task<Result<bool>> ConfigureApiKeyAsync(Guid commandId, ApiKeyRecord key, CancellationToken cancellationToken = default)
        => Send<bool>("/v1/admin/api-keys", new ConfigureApiKeyRequest(key), true, commandId, cancellationToken);
    public Task<Result<NodeStatus>> StatusAsync(CancellationToken cancellationToken = default)
        => Send<NodeStatus>("/v1/status", null, false, null, cancellationToken);
}
