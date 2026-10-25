using System.Runtime.ExceptionServices;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextSettlement
{
    internal static void Release(string root, TextGeneration generation, Guid sourceNodeId, ReadExecutionBudget budget, bool building, bool invalidate, bool completed, bool isCurrent, Action clearCurrent, Action clearUnpublishedBuild, Action<TextFaultStage>? faultObserver, TextProjectionPhysicalGate physicalGate, IOptions<TextExecutionOptions> executionOptions)
    {
        if (invalidate && isCurrent)
        {
            Refresh(root, generation, completed ? budget : null, faultObserver, physicalGate, executionOptions: executionOptions);
            physicalGate.Run(() =>
            {
                TextFiles.DeleteOwnedGeneration(root, generation.Leaf, sourceNodeId, executionOptions: executionOptions, resources: generation.Resources);
                clearCurrent();
            });
            return;
        }
        if (building && !generation.Published)
        {
            physicalGate.Run(generation.DisposeIndex);
            physicalGate.Run(() =>
            {
                TextFiles.DeleteBuildingGeneration(root, generation.Leaf, sourceNodeId, executionOptions: executionOptions, resources: generation.Resources);
                clearUnpublishedBuild();
            });
            return;
        }
        if (generation.CurrentIndex is not null)
        {
            Refresh(root, generation, completed ? budget : null, faultObserver, physicalGate, executionOptions: executionOptions);
        }
    }

    internal static void Refresh(string root, TextGeneration generation, ReadExecutionBudget? budget, Action<TextFaultStage>? faultObserver, TextProjectionPhysicalGate physicalGate, IOptions<TextExecutionOptions> executionOptions)
    {
        var files = physicalGate.Run(() => generation.CloseAndCapture(budget));
        try
        {
            physicalGate.Run(() => CheckForSettlement(generation, budget, executionOptions: executionOptions));
            physicalGate.Run(() => WriteManifest(root, generation, files, budget, executionOptions: executionOptions));
            faultObserver?.Invoke(TextFaultStage.InventoryFlushed);
            physicalGate.Run(() => TextFiles.PublishManifest(root, generation.Leaf, generation.Resources));
            faultObserver?.Invoke(TextFaultStage.ManifestPublished);
        }
        catch (Exception cleanupFailure) when (generation.DeferredBudgetFailure is { } primary)
        {
            generation.ClearDeferredBudgetFailure();
            throw new AggregateException(primary, cleanupFailure);
        }
        ThrowDeferredBudgetFailure(generation);
    }

    internal static void CheckForSettlement(TextGeneration generation, ReadExecutionBudget? budget, IOptions<TextExecutionOptions> executionOptions)
    {
        if (budget is null || generation.DeferredBudgetFailure is not null)
        {
            TextFiles.CheckGenerationBound(generation.Path, executionOptions: executionOptions);
            return;
        }
        try
        {
            TextFiles.CheckGenerationBound(generation.Path, budget: budget, executionOptions: executionOptions);
        }
        catch (OperationCanceledException error)
        {
            generation.DeferBudgetFailure(error);
            TextFiles.CheckGenerationBound(generation.Path, executionOptions: executionOptions);
        }
        catch (KeyLoadException error) when (error.Code == KeyLoad.ErrorCode.BudgetExceeded)
        {
            generation.DeferBudgetFailure(error);
            TextFiles.CheckGenerationBound(generation.Path, executionOptions: executionOptions);
        }
    }

    internal static void ThrowDeferredBudgetFailure(TextGeneration generation)
    {
        if (generation.DeferredBudgetFailure is { } failure)
        {
            generation.ClearDeferredBudgetFailure();
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void WriteManifest(string root, TextGeneration generation, TextFile[] files, ReadExecutionBudget? budget, IOptions<TextExecutionOptions> executionOptions)
    {
        var records = generation.SealRecords();
        try
        {
            TextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope, records, files, budget: generation.DeferredBudgetFailure is null ? budget : null, executionOptions: executionOptions, resources: generation.Resources);
        }
        catch (OperationCanceledException error) when (budget is not null)
        {
            generation.DeferBudgetFailure(error);
            TextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope, records, files, executionOptions: executionOptions, resources: generation.Resources);
        }
        catch (KeyLoadException error) when (budget is not null
            && error.Code == KeyLoad.ErrorCode.BudgetExceeded)
        {
            generation.DeferBudgetFailure(error);
            TextFiles.WritePendingManifest(root, generation.Leaf, generation.Scope, records, files, executionOptions: executionOptions, resources: generation.Resources);
        }
    }
}
