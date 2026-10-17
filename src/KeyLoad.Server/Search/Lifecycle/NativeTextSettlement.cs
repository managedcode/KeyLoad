using System.Runtime.ExceptionServices;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextSettlement
{
    internal static void Release(string root, DatabaseLimits limits, NativeTextGeneration generation, Guid sourceNodeId, ReadExecutionBudget budget, bool building, bool invalidate, bool completed, bool isCurrent, Action clearCurrent, Action clearUnpublishedBuild, Action<NativeTextFaultStage>? faultObserver, NativeTextProjectionPhysicalGate physicalGate, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        if (invalidate && isCurrent)
        {
            Refresh(root, limits, generation, completed ? budget : null, faultObserver, physicalGate, executionOptions: executionOptions);
            physicalGate.Run(() =>
            {
                NativeTextFiles.DeleteOwnedGeneration(root, generation.Leaf, sourceNodeId, limits, executionOptions: executionOptions, resources: generation.Resources);
                clearCurrent();
            });
            return;
        }
        if (building && !generation.Published)
        {
            physicalGate.Run(generation.DisposeIndex);
            physicalGate.Run(() =>
            {
                NativeTextFiles.DeleteBuildingGeneration(root, generation.Leaf, sourceNodeId, limits, executionOptions: executionOptions, resources: generation.Resources);
                clearUnpublishedBuild();
            });
            return;
        }
        if (generation.CurrentIndex is not null)
        {
            Refresh(root, limits, generation, completed ? budget : null, faultObserver, physicalGate, executionOptions: executionOptions);
        }
    }

    internal static void Refresh(string root, DatabaseLimits limits, NativeTextGeneration generation, ReadExecutionBudget? budget, Action<NativeTextFaultStage>? faultObserver, NativeTextProjectionPhysicalGate physicalGate, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        var files = physicalGate.Run(() => generation.CloseAndCapture(budget));
        try
        {
            physicalGate.Run(() => CheckForSettlement(generation, budget, executionOptions: executionOptions));
            physicalGate.Run(() => WriteManifest(root, limits, generation, files, budget, executionOptions: executionOptions));
            faultObserver?.Invoke(NativeTextFaultStage.NativeInventoryFlushed);
            physicalGate.Run(() => NativeTextFiles.PublishManifest(root, generation.Leaf, generation.Resources));
            faultObserver?.Invoke(NativeTextFaultStage.ManifestPublished);
        }
        catch (Exception cleanupFailure) when (generation.DeferredBudgetFailure is { } primary)
        {
            generation.ClearDeferredBudgetFailure();
            throw new AggregateException(primary, cleanupFailure);
        }
        ThrowDeferredBudgetFailure(generation);
    }

    internal static void CheckForSettlement(NativeTextGeneration generation, ReadExecutionBudget? budget, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        if (budget is null || generation.DeferredBudgetFailure is not null)
        {
            NativeTextFiles.CheckGenerationBound(generation.Path, executionOptions: executionOptions);
            return;
        }
        try
        {
            NativeTextFiles.CheckGenerationBound(generation.Path, budget: budget, executionOptions: executionOptions);
        }
        catch (OperationCanceledException error)
        {
            generation.DeferBudgetFailure(error);
            NativeTextFiles.CheckGenerationBound(generation.Path, executionOptions: executionOptions);
        }
        catch (KeyLoadException error) when (error.Code == KeyLoad.ErrorCode.BudgetExceeded)
        {
            generation.DeferBudgetFailure(error);
            NativeTextFiles.CheckGenerationBound(generation.Path, executionOptions: executionOptions);
        }
    }

    internal static void ThrowDeferredBudgetFailure(NativeTextGeneration generation)
    {
        if (generation.DeferredBudgetFailure is { } failure)
        {
            generation.ClearDeferredBudgetFailure();
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void WriteManifest(string root, DatabaseLimits limits, NativeTextGeneration generation, NativeTextFile[] files, ReadExecutionBudget? budget, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        var records = generation.SealRecords();
        try
        {
            NativeTextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope, records, files,
                limits, budget: generation.DeferredBudgetFailure is null ? budget : null, executionOptions: executionOptions, resources: generation.Resources);
        }
        catch (OperationCanceledException error) when (budget is not null)
        {
            generation.DeferBudgetFailure(error);
            NativeTextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope, records, files, limits, executionOptions: executionOptions, resources: generation.Resources);
        }
        catch (KeyLoadException error) when (budget is not null
            && error.Code == KeyLoad.ErrorCode.BudgetExceeded)
        {
            generation.DeferBudgetFailure(error);
            NativeTextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope, records, files, limits, executionOptions: executionOptions, resources: generation.Resources);
        }
    }
}
