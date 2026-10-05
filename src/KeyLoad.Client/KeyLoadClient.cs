using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KeyLoad.Client.Features.ClientApi;
using KeyLoad.Query;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>HTTP SDK. Writes keep caller command IDs; a timeout is an unknown outcome, never an automatic new write.</summary>
public sealed partial class KeyLoadClient
{
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";
    private readonly HttpClient http;
    private readonly string apiKey;
    private readonly KeyLoadClientExecutionOptions execution;

    /// <summary>Creates the authenticated transport with one validated native policy snapshot.</summary>
    /// <param name="http">The caller-owned HTTP transport.</param>
    /// <param name="apiKey">The persisted database credential.</param>
    /// <param name="executionOptions">The caller's centrally bound transport budget.</param>
    public KeyLoadClient(HttpClient http, string apiKey, IOptions<KeyLoadClientExecutionOptions> executionOptions)
    {
        this.http = http;
        this.apiKey = apiKey;
        execution = executionOptions.Value;
        execution.Validate();
    }

    internal async Task<Result<T>> Send<T>(string path, object? request, bool write, Guid? id, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(request is null ? HttpMethod.Get : HttpMethod.Post, path);
        message.Headers.Authorization = new AuthenticationHeaderValue(ClientTransportMessages.BearerScheme, apiKey);
        if (id is { } command)
        {
            message.Headers.Add(CommandIdHeader, command.ToString());
        }

        if (request is not null)
        {
            message.Content = JsonContent.Create(request, request.GetType(), options: JsonDefaults.Options);
        }

        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var problem = await BoundedProblemReader.ReadAsync(response.Content, JsonDefaults.Options, cancellationToken,
                    execution.MaximumProblemBodyBytes)
                    .ConfigureAwait(false);
                return problem ?? Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost, ClientTransportMessages.ServerResponseUnavailable);
            }
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(body, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            return Result<T>.Succeed(value!);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            return Errors.Problem(write ? ErrorCode.UnknownWriteOutcome : cancellationToken.IsCancellationRequested ? ErrorCode.Cancelled : ErrorCode.OwnershipLost,
                write ? ClientTransportMessages.WriteResponseUnavailable : ClientTransportMessages.ReadResponseUnavailable);
        }
    }
    /// <summary>Submits one atomic command using its stable idempotency identifier.</summary>
    /// <param name="command">Typed command and caller-owned stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed receipt or a classified transport/server problem.</returns>
    public Task<Result<CommitReceipt>> CommitAsync(CommandRequest command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Send<CommitReceipt>(ClientApiRoutes.Commands, command, true, command.CommandId, cancellationToken);
    }
    /// <summary>Reads one document through the authenticated public API.</summary>
    /// <param name="reference">Document identity to read.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The document result, including null when absent.</returns>
    public Task<Result<DocumentResult?>> GetAsync(EntityRef reference, CancellationToken cancellationToken = default)
        => Send<DocumentResult?>(ClientApiRoutes.DocumentsGet, new GetDocumentRequest(reference), false, null, cancellationToken);
    /// <summary>Reads a bounded page from one event stream.</summary>
    /// <param name="request">Stream identity, cursor and page limit.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed stream page.</returns>
    public Task<Result<StreamPage>> ReadStreamAsync(ReadStreamRequest request, CancellationToken cancellationToken = default)
        => Send<StreamPage>(ClientApiRoutes.StreamsRead, request, false, null, cancellationToken);
    /// <summary>Reads a bounded page from one event source.</summary>
    /// <param name="request">Source identity, cursor and page limit.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed event-source page.</returns>
    public Task<Result<EventSourcePage>> ReadEventSourceAsync(ReadEventSourceRequest request, CancellationToken cancellationToken = default)
        => Send<EventSourcePage>(ClientApiRoutes.EventsRead, request, false, null, cancellationToken);
    /// <summary>Creates or updates a persisted queue subscription.</summary>
    /// <param name="request">Subscription policy and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting subscription information.</returns>
    public Task<Result<SubscriptionInfo>> ConfigureSubscriptionAsync(ConfigureSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<SubscriptionInfo>(ClientApiRoutes.SubscriptionsConfigure, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Moves a persisted subscription cursor to a requested position.</summary>
    /// <param name="request">Subscription identity and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting subscription information.</returns>
    public Task<Result<SubscriptionInfo>> SeekSubscriptionAsync(SeekSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<SubscriptionInfo>(ClientApiRoutes.SubscriptionsSeek, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Changes the persisted paused state of a subscription.</summary>
    /// <param name="request">Subscription identity, pause state and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting subscription information.</returns>
    public Task<Result<SubscriptionInfo>> SetSubscriptionPausedAsync(SetSubscriptionPausedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<SubscriptionInfo>(ClientApiRoutes.SubscriptionsPause, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Receives a bounded leased page from a subscription.</summary>
    /// <param name="request">Receive identity, subscription and lease limits.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The leased subscription deliveries.</returns>
    public Task<Result<ReceiveSubscriptionResult>> ReceiveSubscriptionAsync(ReceiveSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<ReceiveSubscriptionResult>(ClientApiRoutes.SubscriptionsReceive, request, true, request.RequestId, cancellationToken);
    }
    /// <summary>Completes one leased subscription delivery.</summary>
    /// <param name="request">Delivery acknowledgement and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed receipt.</returns>
    public Task<Result<CommitReceipt>> CompleteSubscriptionAsync(SubscriptionDeliveryCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<CommitReceipt>(ClientApiRoutes.SubscriptionsDelivery, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Commits one subscription projection result and delivery acknowledgement atomically.</summary>
    /// <param name="request">Projection effects, delivery token and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed processing result.</returns>
    public Task<Result<SubscriptionProcessingResult>> CommitSubscriptionProcessingAsync(SubscriptionProcessingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<SubscriptionProcessingResult>(ClientApiRoutes.SubscriptionsProcess, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Reads persisted subscription status.</summary>
    /// <param name="subscription">Subscription identity.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The subscription status.</returns>
    public Task<Result<SubscriptionInfo>> SubscriptionStatusAsync(SubscriptionRef subscription, CancellationToken cancellationToken = default)
        => Send<SubscriptionInfo>(ClientApiRoutes.SubscriptionsStatus, new GetSubscriptionRequest(subscription), false, null, cancellationToken);
    /// <summary>Receives a bounded leased page from a queue.</summary>
    /// <param name="request">Queue identity, receive ID and lease limits.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The leased queue deliveries.</returns>
    public Task<Result<ReceiveResult>> ReceiveAsync(ReceiveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<ReceiveResult>(ClientApiRoutes.QueuesReceive, request, true, request.RequestId, cancellationToken);
    }
    /// <summary>Completes one leased queue delivery.</summary>
    /// <param name="request">Delivery acknowledgement and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed receipt.</returns>
    public Task<Result<CommitReceipt>> CompleteAsync(DeliveryCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<CommitReceipt>(ClientApiRoutes.QueuesDelivery, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Commits one queue-processing result and delivery acknowledgement atomically.</summary>
    /// <param name="request">Processing effects, delivery token and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed receipt.</returns>
    public Task<Result<CommitReceipt>> CommitProcessingAsync(ProcessingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<CommitReceipt>(ClientApiRoutes.QueuesProcess, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Inspects one queue message without leasing it.</summary>
    /// <param name="request">Queue and message identity.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The inspection result, or null when absent.</returns>
    public Task<Result<MessageInspection?>> InspectAsync(InspectMessageRequest request, CancellationToken cancellationToken = default)
        => Send<MessageInspection?>(ClientApiRoutes.QueuesInspect, request, false, null, cancellationToken);
    /// <summary>Executes a parsed query request.</summary>
    /// <param name="request">Typed query and requested page controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed query page.</returns>
    public Task<Result<QueryPage>> QueryAsync(QueryRequest request, CancellationToken cancellationToken = default)
        => Send<QueryPage>(ClientApiRoutes.Query, request, false, null, cancellationToken);
    /// <summary>Executes a canonical AST query request.</summary>
    /// <param name="request">Typed Q1 AST and requested page controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed query page.</returns>
    public Task<Result<QueryPage>> QueryAstAsync(AstQueryRequest request, CancellationToken cancellationToken = default)
        => Send<QueryPage>(ClientApiRoutes.QueryAst, request, false, null, cancellationToken);
    /// <summary>Builds and executes the immutable request from a typed query builder.</summary>
    /// <typeparam name="T">Application record type used to translate the builder expressions.</typeparam>
    /// <param name="query">Typed query builder.</param>
    /// <param name="allowFullScan">Whether to permit a query without a selective predicate.</param>
    /// <param name="cursor">Optional page continuation cursor.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed query page.</returns>
    public Task<Result<QueryPage>> QueryAsync<T>(KeyLoadQuery<T> query, bool allowFullScan = false, string? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return QueryAstAsync(query.ToRequest(allowFullScan, cursor), cancellationToken);
    }
    /// <summary>Reads the server's supported query capabilities.</summary>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The capability manifest.</returns>
    public Task<Result<QueryCapabilityManifest>> QueryCapabilitiesAsync(CancellationToken cancellationToken = default)
        => Send<QueryCapabilityManifest>(ClientApiRoutes.QueryCapabilities, null, false, null, cancellationToken);
    /// <summary>Reads one bounded change-feed page.</summary>
    /// <param name="request">Feed identity and page continuation controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed change-feed page.</returns>
    public Task<Result<ChangeFeedPage>> ReadChangesAsync(ReadChangeFeedRequest request, CancellationToken cancellationToken = default)
        => Send<ChangeFeedPage>(ClientApiRoutes.ChangesRead, request, false, null, cancellationToken);
    /// <summary>Starts a bounded live query snapshot.</summary>
    /// <param name="request">Live query selection and lifetime controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The initial live query snapshot.</returns>
    public Task<Result<LiveQuerySnapshot>> StartLiveQueryAsync(StartLiveQueryRequest request, CancellationToken cancellationToken = default)
        => Send<LiveQuerySnapshot>(ClientApiRoutes.LiveQueryStart, request, false, null, cancellationToken);
    /// <summary>Reads a bounded page from an existing live query.</summary>
    /// <param name="request">Live query identity and page cursor.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed live query page.</returns>
    public Task<Result<LiveQueryPage>> ReadLiveQueryAsync(ReadLiveQueryRequest request, CancellationToken cancellationToken = default)
        => Send<LiveQueryPage>(ClientApiRoutes.LiveQueryRead, request, false, null, cancellationToken);
    /// <summary>Reads the status of one projection outbox.</summary>
    /// <param name="partition">Partition whose outbox is inspected.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The persisted outbox status.</returns>
    public Task<Result<OutboxStatus>> OutboxStatusAsync(PartitionRef partition, CancellationToken cancellationToken = default)
        => Send<OutboxStatus>(ClientApiRoutes.OutboxStatus, new GetOutboxStatusRequest(partition), false, null, cancellationToken);
    /// <summary>Purges an eligible prefix of one projection outbox.</summary>
    /// <param name="request">Purge range, partition and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting outbox head.</returns>
    public Task<Result<OutboxHead>> PurgeOutboxAsync(PurgeOutboxRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<OutboxHead>(ClientApiRoutes.OutboxPurge, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Creates or updates one projection consumer.</summary>
    /// <param name="request">Projection consumer policy and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting consumer information.</returns>
    public Task<Result<ProjectionConsumerInfo>> ConfigureProjectionAsync(ConfigureProjectionConsumerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<ProjectionConsumerInfo>(ClientApiRoutes.ProjectionsConfigure, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Reads one bounded projection outbox batch.</summary>
    /// <param name="request">Consumer identity, cursor and batch limits.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed projection batch.</returns>
    public Task<Result<ProjectionBatch>> ReadProjectionAsync(ReadProjectionBatchRequest request, CancellationToken cancellationToken = default)
        => Send<ProjectionBatch>(ClientApiRoutes.ProjectionsRead, request, false, null, cancellationToken);
    /// <summary>Commits one projection batch result using its stable command identifier.</summary>
    /// <param name="request">Consumer progress and projection effects.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The committed projection result.</returns>
    public Task<Result<ProjectionBatchResult>> CommitProjectionAsync(CommitProjectionBatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<ProjectionBatchResult>(ClientApiRoutes.ProjectionsCommit, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Releases one projection consumer lease.</summary>
    /// <param name="request">Consumer identity and stable command identifier.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting consumer information.</returns>
    public Task<Result<ProjectionConsumerInfo>> ReleaseProjectionAsync(ReleaseProjectionConsumerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Send<ProjectionConsumerInfo>(ClientApiRoutes.ProjectionsRelease, request, true, request.CommandId, cancellationToken);
    }
    /// <summary>Runs a bounded nearest-neighbor or hybrid search.</summary>
    /// <param name="request">Search collection, vector/text query and limits.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The ranked documents.</returns>
    public Task<Result<RankedDocument[]>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        => Send<RankedDocument[]>(ClientApiRoutes.Search, request, false, null, cancellationToken);
    /// <summary>Traverses a bounded graph from the requested starting nodes.</summary>
    /// <param name="request">Graph traversal start, direction and limits.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed graph traversal result.</returns>
    public Task<Result<GraphTraversal>> TraverseAsync(TraverseRequest request, CancellationToken cancellationToken = default)
        => Send<GraphTraversal>(ClientApiRoutes.GraphTraverse, request, false, null, cancellationToken);
    /// <summary>Reads a bounded page of time-series samples.</summary>
    /// <param name="request">Series identity, time range and page controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed sample page.</returns>
    public Task<Result<SampleRecord[]>> ReadSamplesAsync(ReadSamplesRequest request, CancellationToken cancellationToken = default)
        => Send<SampleRecord[]>(ClientApiRoutes.SeriesRead, request, false, null, cancellationToken);
    /// <summary>Creates or updates a persisted resource definition.</summary>
    /// <param name="commandId">Stable command identifier for the write.</param>
    /// <param name="request">Resource schema and authorization policy.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting resource definition.</returns>
    public Task<Result<ResourceDefinition>> ConfigureResourceAsync(Guid commandId, ConfigureResourceRequest request, CancellationToken cancellationToken = default)
        => Send<ResourceDefinition>(ClientApiRoutes.ResourcesConfigure, request, true, commandId, cancellationToken);
    /// <summary>Creates or updates one persisted principal.</summary>
    /// <param name="commandId">Stable command identifier for the write.</param>
    /// <param name="principal">Principal identity and policy data.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The resulting principal record.</returns>
    public Task<Result<PrincipalRecord>> ConfigurePrincipalAsync(Guid commandId, PrincipalRecord principal, CancellationToken cancellationToken = default)
        => Send<PrincipalRecord>(ClientApiRoutes.PrincipalsConfigure, new ConfigurePrincipalRequest(principal), true, commandId, cancellationToken);
    /// <summary>Creates or updates one persisted API key.</summary>
    /// <param name="commandId">Stable command identifier for the write.</param>
    /// <param name="key">API key metadata and policy.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>True when the write is committed.</returns>
    public Task<Result<bool>> ConfigureApiKeyAsync(Guid commandId, ApiKeyRecord key, CancellationToken cancellationToken = default)
        => Send<bool>(ClientApiRoutes.ApiKeysConfigure, new ConfigureApiKeyRequest(key), true, commandId, cancellationToken);
    /// <summary>Reads public node readiness and status information.</summary>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The node status.</returns>
    public Task<Result<NodeStatus>> StatusAsync(CancellationToken cancellationToken = default)
        => Send<NodeStatus>(ClientApiRoutes.Status, null, false, null, cancellationToken);
    /// <summary>Reads operator-visible node admission status.</summary>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The node admission status.</returns>
    public Task<Result<NodeAdmissionStatus>> AdmissionStatusAsync(CancellationToken cancellationToken = default)
        => Send<NodeAdmissionStatus>(ClientApiRoutes.AdmissionStatus, null, false, null, cancellationToken);
}
