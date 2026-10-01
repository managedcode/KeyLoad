using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KeyLoad.Query;
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
    public Task<Result<EventSourcePage>> ReadEventSourceAsync(ReadEventSourceRequest request, CancellationToken cancellationToken = default)
        => Send<EventSourcePage>("/v1/events/read", request, false, null, cancellationToken);
    public Task<Result<SubscriptionInfo>> ConfigureSubscriptionAsync(ConfigureSubscriptionRequest request, CancellationToken cancellationToken = default)
        => Send<SubscriptionInfo>("/v1/subscriptions/configure", request, true, request.CommandId, cancellationToken);
    public Task<Result<SubscriptionInfo>> SeekSubscriptionAsync(SeekSubscriptionRequest request, CancellationToken cancellationToken = default)
        => Send<SubscriptionInfo>("/v1/subscriptions/seek", request, true, request.CommandId, cancellationToken);
    public Task<Result<SubscriptionInfo>> SetSubscriptionPausedAsync(SetSubscriptionPausedRequest request, CancellationToken cancellationToken = default)
        => Send<SubscriptionInfo>("/v1/subscriptions/pause", request, true, request.CommandId, cancellationToken);
    public Task<Result<ReceiveSubscriptionResult>> ReceiveSubscriptionAsync(ReceiveSubscriptionRequest request, CancellationToken cancellationToken = default)
        => Send<ReceiveSubscriptionResult>("/v1/subscriptions/receive", request, true, request.RequestId, cancellationToken);
    public Task<Result<CommitReceipt>> CompleteSubscriptionAsync(SubscriptionDeliveryCommand request, CancellationToken cancellationToken = default)
        => Send<CommitReceipt>("/v1/subscriptions/delivery", request, true, request.CommandId, cancellationToken);
    public Task<Result<SubscriptionProcessingResult>> CommitSubscriptionProcessingAsync(SubscriptionProcessingRequest request, CancellationToken cancellationToken = default)
        => Send<SubscriptionProcessingResult>("/v1/subscriptions/process", request, true, request.CommandId, cancellationToken);
    public Task<Result<SubscriptionInfo>> SubscriptionStatusAsync(SubscriptionRef subscription, CancellationToken cancellationToken = default)
        => Send<SubscriptionInfo>("/v1/subscriptions/status", new GetSubscriptionRequest(subscription), false, null, cancellationToken);
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
    public Task<Result<QueryPage>> QueryAstAsync(AstQueryRequest request, CancellationToken cancellationToken = default)
        => Send<QueryPage>("/v1/query/ast", request, false, null, cancellationToken);
    public Task<Result<QueryPage>> QueryAsync<T>(KeyLoadQuery<T> query, bool allowFullScan = false, string? cursor = null, CancellationToken cancellationToken = default)
        => QueryAstAsync(query.ToRequest(allowFullScan, cursor), cancellationToken);
    public Task<Result<QueryCapabilityManifest>> QueryCapabilitiesAsync(CancellationToken cancellationToken = default)
        => Send<QueryCapabilityManifest>("/v1/query/capabilities", null, false, null, cancellationToken);
    public Task<Result<ChangeFeedPage>> ReadChangesAsync(ReadChangeFeedRequest request, CancellationToken cancellationToken = default)
        => Send<ChangeFeedPage>("/v1/changes/read", request, false, null, cancellationToken);
    public Task<Result<LiveQuerySnapshot>> StartLiveQueryAsync(StartLiveQueryRequest request, CancellationToken cancellationToken = default)
        => Send<LiveQuerySnapshot>("/v1/query/live/start", request, false, null, cancellationToken);
    public Task<Result<LiveQueryPage>> ReadLiveQueryAsync(ReadLiveQueryRequest request, CancellationToken cancellationToken = default)
        => Send<LiveQueryPage>("/v1/query/live/read", request, false, null, cancellationToken);
    public Task<Result<OutboxStatus>> OutboxStatusAsync(PartitionRef partition, CancellationToken cancellationToken = default)
        => Send<OutboxStatus>("/v1/admin/outbox/status", new GetOutboxStatusRequest(partition), false, null, cancellationToken);
    public Task<Result<OutboxHead>> PurgeOutboxAsync(PurgeOutboxRequest request, CancellationToken cancellationToken = default)
        => Send<OutboxHead>("/v1/admin/outbox/purge", request, true, request.CommandId, cancellationToken);
    public Task<Result<ProjectionConsumerInfo>> ConfigureProjectionAsync(ConfigureProjectionConsumerRequest request, CancellationToken cancellationToken = default)
        => Send<ProjectionConsumerInfo>("/v1/admin/projections/configure", request, true, request.CommandId, cancellationToken);
    public Task<Result<ProjectionBatch>> ReadProjectionAsync(ReadProjectionBatchRequest request, CancellationToken cancellationToken = default)
        => Send<ProjectionBatch>("/v1/admin/projections/read", request, false, null, cancellationToken);
    public Task<Result<ProjectionBatchResult>> CommitProjectionAsync(CommitProjectionBatchRequest request, CancellationToken cancellationToken = default)
        => Send<ProjectionBatchResult>("/v1/admin/projections/commit", request, true, request.CommandId, cancellationToken);
    public Task<Result<ProjectionConsumerInfo>> ReleaseProjectionAsync(ReleaseProjectionConsumerRequest request, CancellationToken cancellationToken = default)
        => Send<ProjectionConsumerInfo>("/v1/admin/projections/release", request, true, request.CommandId, cancellationToken);
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
