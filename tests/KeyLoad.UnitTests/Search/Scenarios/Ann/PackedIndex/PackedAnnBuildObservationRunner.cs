using System.Globalization;
using System.Runtime.ExceptionServices;
using KeyLoad.Query.Features.Search;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnBuildObservationRunner
{
    private const string ObservationPrefix = "ANN_BUILD_OBSERVATION";
    private const string BuildPhase = "Build";
    private const string NoErrorCode = "none";
    private const string MultipleFailures = "The ANN build and its diagnostic output failed.";

    internal static PackedAnnIndex Build(PackedAnnBuildScenario scenario, VectorSpace space,
        IReadOnlyList<VectorRecord> records, PackedAnnOptions options, AnnWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        PackedAnnIndex? index = null;
        Exception? buildFailure = null;
        var elapsedTicks = 0L;
        var allocatedBytes = 0L;
        try
        {
            index = PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(options), budget);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            elapsedTicks = clock.GetTimestamp() - started;
            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            buildFailure = failure;
        }
        catch (Exception fatal) when (CqrsRuntimeFailures.FindFatal(fatal) is not null)
        {
            elapsedTicks = clock.GetTimestamp() - started;
            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            buildFailure = fatal;
        }
        if (buildFailure is null)
        {
            elapsedTicks = clock.GetTimestamp() - started;
            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        }
        var observation = CreateObservation(scenario, space, records.Count, options, budget,
            buildFailure, elapsedTicks, allocatedBytes, clock.TimestampFrequency);
        ThrowFailures(buildFailure, WriteObservation(observation));
        return index!;
    }

    private static Exception? WriteObservation(PackedAnnBuildObservation observation)
    {
        Exception? outputFailure = null;
        try
        {
            TestContext.Current!.Output.WriteLine(Format(observation));
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            outputFailure = failure;
        }
        catch (Exception fatal) when (CqrsRuntimeFailures.FindFatal(fatal) is not null)
        {
            outputFailure = fatal;
        }
        return outputFailure;
    }

    private static PackedAnnBuildObservation CreateObservation(PackedAnnBuildScenario scenario,
        VectorSpace space, int recordCount, PackedAnnOptions options, AnnWorkBudget budget,
        Exception? failure, long elapsedTicks, long allocatedBytes, long timestampFrequency)
    {
        var outcome = failure switch
        {
            null => PackedAnnBuildOutcome.Success,
            OperationCanceledException => PackedAnnBuildOutcome.Canceled,
            KeyLoadException { Code: ErrorCode.BudgetExceeded } => PackedAnnBuildOutcome.BudgetExceeded,
            _ => PackedAnnBuildOutcome.Failure
        };
        ErrorCode? errorCode = failure is KeyLoadException keyLoadFailure ? keyLoadFailure.Code : null;
        return new(scenario, recordCount, space.Dimension, space.Metric, options.Connections,
            options.EfConstruction, options.EfSearch, options.MaxLevel, options.ExactThreshold,
            options.MaxRecords, options.MaxIndexBytes, options.MaxScratchBytes, options.Seed,
            outcome, errorCode, timestampFrequency, elapsedTicks, allocatedBytes, budget.WorkUnits,
            budget.DistanceEvaluations, budget.EdgeVisits);
    }

    private static string Format(PackedAnnBuildObservation observation)
    {
        var errorCode = observation.FailureCode?.ToString() ?? NoErrorCode;
        return string.Create(CultureInfo.InvariantCulture,
            $"{ObservationPrefix} phase={BuildPhase} scenario={observation.Scenario} rows={observation.RecordCount} dimension={observation.Dimension} metric={observation.Metric} connections={observation.Connections} efConstruction={observation.EfConstruction} efSearch={observation.EfSearch} maxLevel={observation.MaxLevel} exactThreshold={observation.ExactThreshold} maxRecords={observation.MaxRecords} maxIndexBytes={observation.MaxIndexBytes} maxScratchBytes={observation.MaxScratchBytes} seed={observation.Seed} outcome={observation.Outcome} errorCode={errorCode} stopwatchFrequency={observation.StopwatchFrequency} elapsedStopwatchTicks={observation.ElapsedStopwatchTicks} allocatedBytes={observation.AllocatedBytes} workUnits={observation.WorkUnits} distanceEvaluations={observation.DistanceEvaluations} edgeVisits={observation.EdgeVisits}");
    }

    private static void ThrowFailures(Exception? buildFailure, Exception? outputFailure)
    {
        if (buildFailure is null && outputFailure is null)
        {
            return;
        }
        if (buildFailure is null || outputFailure is null)
        {
            ExceptionDispatchInfo.Capture(buildFailure ?? outputFailure!).Throw();
            return;
        }
        var failures = new List<Exception> { buildFailure!, outputFailure! };
        PrioritizeFatal(failures);
        throw new AggregateException(MultipleFailures, failures);
    }

    private static void PrioritizeFatal(List<Exception> failures)
    {
        for (var index = 0; index < failures.Count; index++)
        {
            if (CqrsRuntimeFailures.FindFatal(failures[index]) is not null)
            {
                if (index > 0)
                {
                    (failures[0], failures[index]) = (failures[index], failures[0]);
                }
                return;
            }
        }
    }
}
