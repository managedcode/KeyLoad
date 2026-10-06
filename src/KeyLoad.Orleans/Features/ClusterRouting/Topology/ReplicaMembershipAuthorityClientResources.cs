using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipAuthorityClientResources : IAsyncDisposable
{
    private readonly OwnedResources resources;
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly TimeSpan requestTimeout;

    internal ReplicaMembershipAuthorityClientResources(ReplicaMembershipAuthorityExchangeOptions options,
        IOptions<OrleansMembershipOptions> membershipOptions, IOptions<ReplicaExecutionOptions> executionOptions)
    {
        var execution = executionOptions.Value;
        requestTimeout = execution.CommandTimeout + execution.ReadBarrierTimeout;
        resources = new(options, membershipOptions);
        lifetime = new(resources.Stopping, resources.Dispose);
    }

    internal async Task<ReplicaMembershipAuthorityReplyV1> SendAsync(ReplicaMembershipAuthorityCallV1 call,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var operation = lifetime.TryEnter()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable);
        using (operation)
        using (var deadline = new CancellationTokenSource(requestTimeout, clock))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            operation.ShutdownToken, deadline.Token))
        {
            return await SendAdmittedAsync(call, linked.Token).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.DisposeAsync().ConfigureAwait(false);
        resources.Dispose();
    }

    private async Task<ReplicaMembershipAuthorityReplyV1> SendAdmittedAsync(
        ReplicaMembershipAuthorityCallV1 call, CancellationToken cancellationToken)
    {
        await resources.Admission.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await resources.Exchange.SendAsync(call, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            resources.Admission.Release();
        }
    }

    private sealed class OwnedResources : IDisposable
    {
        private readonly ReplicaMembershipAuthorityExchange exchange;
        private readonly SemaphoreSlim admission;
        private readonly CancellationTokenSource stopping = new();
        private int disposed;

        internal OwnedResources(ReplicaMembershipAuthorityExchangeOptions options,
            IOptions<OrleansMembershipOptions> membershipOptions)
        {
            var settings = membershipOptions.Value;
            admission = new(settings.MaximumAdmissions, settings.MaximumAdmissions);
            exchange = new(options, membershipOptions);
        }

        internal ReplicaMembershipAuthorityExchange Exchange => exchange;
        internal SemaphoreSlim Admission => admission;
        internal CancellationTokenSource Stopping => stopping;

        public void Dispose()
        {
            const int ValueSingleItemCount = 1;
            const int EmptyExchange = 0;

            if (Interlocked.Exchange(ref disposed, ValueSingleItemCount) != EmptyExchange)
            {
                return;
            }

            var failures = new List<Exception>(3);
            DisposeExchange(failures);
            DisposeAdmission(failures);
            DisposeStopping(failures);
            ReplicaDiscoveryLifetime.ThrowIfAny(failures);
        }

        private void DisposeExchange(List<Exception> failures)
        {
            try
            {
                exchange.Dispose();
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }

        private void DisposeAdmission(List<Exception> failures)
        {
            try
            {
                admission.Dispose();
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }

        private void DisposeStopping(List<Exception> failures)
        {
            try
            {
                stopping.Dispose();
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
    }
}
