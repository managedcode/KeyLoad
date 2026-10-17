namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.Alias(CommandObservationNativeProtocol.ActionAlias)]
internal enum CommandObservationNativeAction
{
    Execute = 1,
    Read = 2,
    Abort = 3
}
