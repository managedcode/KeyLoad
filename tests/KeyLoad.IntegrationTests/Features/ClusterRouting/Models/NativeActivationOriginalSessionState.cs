namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Retains the actual original command, complete receipt and original token without reissuing either identity.</summary>
internal sealed record NativeActivationOriginalSessionState(CommandRequest Command, CommitReceipt Receipt,
    CommitReceipt ExpectedReceipt, PrincipalRecord ReaderPrincipal, RequestCqrsRf3Callers Readers)
{
    internal CommitToken Minimum => Receipt.Token;
    internal EntityRef Reference => new(Command.Partition, RequestCqrsRf3Protocol.AdminCollection,
        RequestCqrsRf3Protocol.DocumentId);
    public override string ToString() => "NativeActivationOriginalSessionState(<private>)";
}
