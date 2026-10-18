using KeyLoad.Client.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
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
    /// <returns>An inspection response whose Message field is null when absent, or a classified problem.</returns>
    public Task<Result<MessageInspectionResult>> InspectAsync(InspectMessageRequest request, CancellationToken cancellationToken = default)
        => Send<MessageInspectionResult>(ClientApiRoutes.QueuesInspect, request, false, null, cancellationToken);
}
