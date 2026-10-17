namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferRf3Protocol
{
    internal const string SubjectSetting = "KeyLoad__RemoteTransferPrincipalId";
    private static readonly string[] targetNodes = ["node4", "node5", "node6"];
    internal static ReadOnlySpan<string> TargetNodes => targetNodes;
}
