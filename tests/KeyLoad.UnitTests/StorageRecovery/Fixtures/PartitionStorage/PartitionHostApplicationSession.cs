using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Replication;
using KeyLoad.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionHostApplicationSession
{
    internal PartitionHostApplicationSession(NodeOptions options)
    {
        Owner = new(ServerRuntimeTestOptions.Runtime(options), new AuthorizationPolicy(),
            new CommandAdmissionGovernor(UnitAdmissionOptions.Command(options.CommandAdmission)),
            TimeProvider.System, NullLogger<ReplicaConsensus>.Instance);
    }

    internal PartitionHostApplicationOwner Owner
    {
        get;
    }

    internal Exception? ExpectedOpeningFailure
    {
        get; set;
    }

    internal async Task CloseAsync(List<Exception> failures)
    {
        var closing = Owner.DisposeAsync().AsTask();
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => closing, observed).ConfigureAwait(false);
        failures.AddRange(observed.Where(error => !ReferenceEquals(error, ExpectedOpeningFailure)));
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(ReferenceEquals(closing, Owner.DisposeAsync().AsTask())).IsTrue();
            await Assert.That(Owner.CanReleaseRoot).IsTrue();
        }, failures).ConfigureAwait(false);
    }
}
