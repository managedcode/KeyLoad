namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeProtocol
{
    internal const int Version = 1;
    internal const int SelectedVoter = 0;
    internal const string ActionAlias = "keyload.server.remote-graph-native-action.v1";
    internal const string ReplyAlias = "keyload.server.remote-graph-native-reply.v1";
    internal const string RequestAlias = "keyload.server.remote-graph-native-request.v1";
    internal const string ServiceAlias = "keyload.server.remote-graph-grain-service.v1";
    internal const string ExchangeAlias = "exchange";
    internal const string PhaseIdentityAlias = "keyload.server.remote-graph-phase-command-identity.v1";
    internal const string RequestPurpose = "keyload-remote-graph-native-request-v1";
    internal const string ReplyPurpose = "keyload-remote-graph-native-reply-v1";
    internal const string InvalidProof = "The remote graph peer proof is invalid.";
    internal const string Unavailable = "The remote graph owner is unavailable.";

    internal static bool MayHaveEffects(RemoteGraphNativeAction action)
        => action == RemoteGraphNativeAction.ReceiverApply;
}
