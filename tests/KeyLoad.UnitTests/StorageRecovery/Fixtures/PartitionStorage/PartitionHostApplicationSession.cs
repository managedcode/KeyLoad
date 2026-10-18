using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionHostApplicationSession
{
    private readonly RequestCqrsHostedRuntimeFixture native;

    private PartitionHostApplicationSession(NodeOptions options, RequestCqrsHostedRuntimeFixture native)
    {
        this.native = native;
        Owner = new(ServerRuntimeTestOptions.Runtime(options), new AuthorizationPolicy(),
            new CommandAdmissionGovernor(UnitAdmissionOptions.Command(options.CommandAdmission)),
            TimeProvider.System, NullLogger<PartitionHost>.Instance, native.Grains);
    }

    internal static async Task<PartitionHostApplicationSession> CreateAsync(NodeOptions options)
    {
        var native = new RequestCqrsHostedRuntimeFixture();
        await native.InitializeAsync(TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        try
        {
            return new(options, native);
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            await ServerFailureObserver.ObserveAsync(() => native.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
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
        await ServerFailureObserver.ObserveAsync(() => native.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
    }
}
