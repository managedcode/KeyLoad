using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Executes an explicit version-fenced queue dispatch command through the existing SDK transport.</summary>
public static class QueueDispatchClient
{
    private const string InvalidCommandId = "A nonempty stable command ID is required.";

    /// <summary>Pauses or resumes only the selected persisted queue.</summary>
    /// <param name="client">The authenticated existing client.</param>
    /// <param name="commandId">The original stable command identity used for replay.</param>
    /// <param name="request">The selected queue, desired pause state and expected version.</param>
    /// <param name="cancellationToken">Cancellation for the complete native operation.</param>
    /// <returns>The complete committed resource definition or the original typed failure.</returns>
    public static Task<Result<ResourceDefinition>> SetQueueDispatchAsync(this KeyLoadClient client,
        Guid commandId, QueueDispatchControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (commandId == Guid.Empty)
        { throw new ArgumentException(InvalidCommandId, nameof(commandId)); }
        return client.Send<ResourceDefinition>(QueueDispatchControlProtocol.Route, request,
            true, commandId, cancellationToken, HttpMethod.Post);
    }
}
