namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record RemoteTransferPostAwaitNativeCut(string Outcome, string Receipt, string Capacity,
    CommitReceipt OriginalReceipt)
{
    internal async Task RequireAsync(RemoteTransferPostAwaitNativeCut actual)
    {
        await Assert.That(actual.Outcome).IsEqualTo(Outcome);
        await Assert.That(actual.Receipt).IsEqualTo(Receipt);
        await Assert.That(actual.Capacity).IsEqualTo(Capacity);
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.EqualAsync(OriginalReceipt, actual.OriginalReceipt);
    }
}
