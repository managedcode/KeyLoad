using System.Runtime.ExceptionServices;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextSettlement
{
    internal static void Release(string root, DatabaseLimits limits, NativeTextGeneration generation,
        Guid sourceNodeId, ReadExecutionBudget budget, bool building, bool invalidate, bool completed, bool isCurrent,
        Action clearCurrent, Action<NativeTextFaultStage>? faultObserver)
    {
        if (invalidate && isCurrent)
        {
            Refresh(root, limits, generation, completed ? budget : null, faultObserver);
            NativeTextFiles.DeleteOwnedGeneration(root, generation.Leaf, sourceNodeId, limits);
            clearCurrent();
            return;
        }
        if (building && !generation.Published)
        {
            generation.DisposeIndex();
            NativeTextFiles.DeleteBuildingGeneration(root, generation.Leaf, sourceNodeId, limits);
            return;
        }
        if (generation.CurrentIndex is not null)
        {
            Refresh(root, limits, generation, completed ? budget : null, faultObserver);
        }
    }

    internal static void Refresh(string root, DatabaseLimits limits, NativeTextGeneration generation,
        ReadExecutionBudget? budget, Action<NativeTextFaultStage>? faultObserver)
    {
        var files = generation.CloseAndCapture(budget);
        try
        {
            CheckForSettlement(generation, budget);
            WriteManifest(root, limits, generation, files, budget);
            faultObserver?.Invoke(NativeTextFaultStage.NativeInventoryFlushed);
            NativeTextFiles.PublishManifest(root, generation.Leaf);
            faultObserver?.Invoke(NativeTextFaultStage.ManifestPublished);
        }
        catch (Exception cleanupFailure) when (generation.DeferredBudgetFailure is { } primary)
        {
            generation.ClearDeferredBudgetFailure();
            throw new AggregateException(primary, cleanupFailure);
        }
        ThrowDeferredBudgetFailure(generation);
    }

    internal static void CheckForSettlement(NativeTextGeneration generation, ReadExecutionBudget? budget)
    {
        if (budget is null || generation.DeferredBudgetFailure is not null)
        {
            NativeTextFiles.CheckGenerationBound(generation.Path);
            return;
        }
        try
        {
            NativeTextFiles.CheckGenerationBound(generation.Path, budget);
        }
        catch (OperationCanceledException error)
        {
            generation.DeferBudgetFailure(error);
            NativeTextFiles.CheckGenerationBound(generation.Path);
        }
        catch (KeyLoadException error) when (error.Code == KeyLoad.ErrorCode.BudgetExceeded)
        {
            generation.DeferBudgetFailure(error);
            NativeTextFiles.CheckGenerationBound(generation.Path);
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

    private static void WriteManifest(string root, DatabaseLimits limits, NativeTextGeneration generation,
        NativeTextFile[] files, ReadExecutionBudget? budget)
    {
        var records = generation.SealRecords();
        try
        {
            NativeTextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope, records, files,
                limits, generation.DeferredBudgetFailure is null ? budget : null);
        }
        catch (OperationCanceledException error) when (budget is not null)
        {
            generation.DeferBudgetFailure(error);
            NativeTextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope, records, files, limits);
        }
        catch (KeyLoadException error) when (budget is not null
            && error.Code == KeyLoad.ErrorCode.BudgetExceeded)
        {
            generation.DeferBudgetFailure(error);
            NativeTextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope, records, files, limits);
        }
    }
}
