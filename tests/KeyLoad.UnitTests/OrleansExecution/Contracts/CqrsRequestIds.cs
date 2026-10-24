namespace KeyLoad.UnitTests.OrleansExecution;

internal static class CqrsRequestIds
{
    internal static readonly Guid Allowed = Guid.Parse(CqrsProtocol.AllowedGuid);
    internal static readonly Guid Denied = Guid.Parse(CqrsProtocol.DeniedGuid);
    internal static readonly Guid ConcurrentOne = Guid.Parse(CqrsProtocol.ConcurrentOneGuid);
    internal static readonly Guid ConcurrentTwo = Guid.Parse(CqrsProtocol.ConcurrentTwoGuid);
    internal static readonly Guid Cancellation = Guid.Parse(CqrsProtocol.CancellationGuid);
    internal static readonly Guid Disposal = Guid.Parse(CqrsProtocol.DisposalGuid);
    internal static readonly Guid HealthyAfterCancellation = Guid.Parse(CqrsProtocol.HealthyAfterCancellationGuid);
    internal static readonly Guid HealthyAfterDisposal = Guid.Parse(CqrsProtocol.HealthyAfterDisposalGuid);
    internal static readonly Guid Failure = Guid.Parse(CqrsProtocol.FailureGuid);
}
