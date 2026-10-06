using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class RequestCqrsReaderCapabilityAssertions
{
    internal static async Task AssertIncompatibleAsync(KeyLoadException? failure)
    {
        var observed = failure ?? throw new InvalidOperationException("The current-reader incompatibility was not rejected.");
        await Assert.That(observed.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(observed.Message).IsEqualTo(ReplicaTransportProtocol.IncompatibleCohort);
    }
}
