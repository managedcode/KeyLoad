namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeCqrsRequestIds
{
    internal static readonly Guid Allowed = Guid.Parse(NativeCqrsProtocol.AllowedGuid);
    internal static readonly Guid Denied = Guid.Parse(NativeCqrsProtocol.DeniedGuid);
    internal static readonly Guid ConcurrentOne = Guid.Parse(NativeCqrsProtocol.ConcurrentOneGuid);
    internal static readonly Guid ConcurrentTwo = Guid.Parse(NativeCqrsProtocol.ConcurrentTwoGuid);
    internal static readonly Guid Cancellation = Guid.Parse(NativeCqrsProtocol.CancellationGuid);
    internal static readonly Guid Disposal = Guid.Parse(NativeCqrsProtocol.DisposalGuid);
    internal static readonly Guid HealthyAfterCancellation = Guid.Parse(NativeCqrsProtocol.HealthyAfterCancellationGuid);
    internal static readonly Guid HealthyAfterDisposal = Guid.Parse(NativeCqrsProtocol.HealthyAfterDisposalGuid);
    internal static readonly Guid Failure = Guid.Parse(NativeCqrsProtocol.FailureGuid);
}
