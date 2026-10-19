using System.Runtime.ExceptionServices;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server;

/// <summary>Runs native work outside the canonical read gate and joins it before publishing or releasing its directory.</summary>
internal sealed class UserAnalyticsGenerationExecution(PartitionHost physical)
{
    private const int NoCleanupFailures = 0;
    private const int PrimaryFailureIndex = 0;
    private AnalyticsStoreNativeGeneration? retainedGeneration;
    private readonly List<Exception> cleanup = [];
    internal IReadOnlyList<Exception> CleanupFailures => cleanup;
    internal bool HasUnjoinedEngine => retainedGeneration is { HasJoinedEngine: false };

    internal NativeAnalyticsAggregate Execute(AnalyticsAggregateSnapshot snapshot,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        snapshot.Work.RequireResourceLifetimeBinding(binding);
        physical.Database.ValidateAnalyticsAggregatePublication(snapshot);
        var directory = UserAnalyticsGenerationPaths.Create(physical, snapshot);
        AnalyticsStoreNativeGeneration? generation = null;
        AnalyticsAggregateResult? result = null;
        Exception? failure = null;
        try
        {
            generation = new(snapshot, directory);
            retainedGeneration = generation;
            result = generation.Query(cancellationToken);
        }
        catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original))
        {
            failure = original;
        }
        catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original))
        {
            failure = original;
        }
        if (generation is not null)
        {
            ServerFailureObserver.Observe(generation.Dispose, cleanup);
            if (generation.HasJoinedEngine)
            {
                retainedGeneration = null;
                ServerFailureObserver.Observe(() => UserAnalyticsGenerationPaths.RemoveJoined(directory), cleanup);
            }
        }
        if (cleanup.Count != NoCleanupFailures)
        {
            if (failure is not null)
            {
                cleanup.Insert(PrimaryFailureIndex, failure);
            }
            ServerFailureObserver.ThrowIfAny(cleanup);
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        physical.Database.ValidateAnalyticsAggregatePublication(snapshot);
        return new(result ?? throw Errors.Fail(ErrorCode.Corruption, MissingResult), snapshot);
    }

    private const string MissingResult = "The native analytics result is missing.";
}
