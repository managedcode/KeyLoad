using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionHostApplicationSession : IAsyncDisposable
{
    private readonly RequestCqrsHostedRuntimeFixture native = new();
    private PartitionHostApplicationOwner? owner;
    private readonly Lock lifecycle = new();
    private Task? closing;

    private PartitionHostApplicationSession() { }

    internal static async Task<PartitionHostApplicationSession> CreateAsync(NodeOptions options)
    {
        var session = new PartitionHostApplicationSession();
        try
        {
            await session.native.InitializeAsync(TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
            session.owner = new(ServerRuntimeTestOptions.Runtime(options), new AuthorizationPolicy(),
                new CommandAdmissionGovernor(UnitAdmissionOptions.Command(options.CommandAdmission)),
                TimeProvider.System, NullLogger<PartitionHost>.Instance, session.native.Grains);
            return session;
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            catch (Exception cleanup) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal PartitionHostApplicationOwner Owner => owner
        ?? throw new InvalidOperationException(nameof(PartitionHostApplicationOwner));

    internal Exception? ExpectedOpeningFailure
    {
        get; set;
    }

    internal Task CloseAsync(List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), failures);

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(closing ??= CloseCoreAsync());
        }
    }

    private async Task CloseCoreAsync()
    {
        await Task.Yield();
        var failures = new List<Exception>();
        if (owner is not null)
        {
            var closing = owner.DisposeAsync().AsTask();
            var observed = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => closing, observed).ConfigureAwait(false);
            failures.AddRange(observed.Where(error => !ReferenceEquals(error, ExpectedOpeningFailure)));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(ReferenceEquals(closing, owner.DisposeAsync().AsTask())).IsTrue();
                await Assert.That(owner.CanReleaseRoot).IsTrue();
            }, failures).ConfigureAwait(false);
        }
        try
        {
            await native.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanup) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
        catch (Exception cleanup) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
