namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeObservedExchange
{
    internal static RemoteModelAuthenticatedReply Complete(RemoteModelNativeReply reply, RemoteModelNativeRequest request,
        bool effects)
    {
        // A correctly authenticated receiver refusal is interpreted only after exact dispatch identity checks.
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, RemoteModelNativeProtocol.Unavailable);
        }
        return new(reply.Payload, reply.Signature, request.DestinationSiloAddress)
        {
            MayHaveEffects = effects
        };
    }
}
