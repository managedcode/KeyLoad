using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal static class IsolatedVectorHostApplication
{
    private const string Unsupported = "unsupported";
    private const string Failed = "failed";
    private const string KeyLoadCapabilityReason = "KeyLoad SDK does not expose persisted vector readback or native numeric predicates required for scaled vector qualification.";

    internal static async Task<int> RunAsync(IsolatedHostTargetOwner owner, IsolatedHostSettings settings,
        CancellationToken cancellationToken)
    {
        var profile = settings.Selection.VectorProfile ?? throw new InvalidOperationException(IsolatedHostConstants.Failure);
        var reason = UnsupportedReason(settings.Selection.Target, profile);
        if (reason is not null)
        {
            await WriteAsync(settings, Unsupported, reason, null, cancellationToken);
            return ComparisonHostConstants.SuccessfulExitCode;
        }
        try
        {
            var target = owner.CreateVector(settings);
            var report = await new VectorComparisonRunner(profile, owner.ExecutionOptions)
                .RunAsync(target, settings.Worker.SourceRevision, settings.Storage, cancellationToken);
            report = report with
            {
                Provenance = settings.Identity.Provenance,
                LoadGeneratorImage = settings.Identity.LoadGeneratorImage
            };
            ValidateNativeVectorReport(report, settings);
            await WriteAsync(settings, IsolatedHostConstants.Measured, null, report, cancellationToken);
            return ComparisonHostConstants.SuccessfulExitCode;
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await WriteAsync(settings, Failed, failure.GetType().Name, null, cancellationToken);
            return ComparisonHostConstants.FailedExitCode;
        }
    }

    private static string? UnsupportedReason(string target, VectorComparisonProfile profile)
    {
        if (target == IsolatedHostConstants.KeyLoad)
        {
            return KeyLoadCapabilityReason;
        }
        var supported = target switch
        {
            IsolatedHostConstants.Postgres => profile.IndexKind is VectorIndexKind.Exact or VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat,
            IsolatedHostConstants.Qdrant or IsolatedHostConstants.SurrealDb => profile.IndexKind is VectorIndexKind.Exact or VectorIndexKind.Hnsw,
            IsolatedHostConstants.HelixDb => profile.IndexKind == VectorIndexKind.NativeAnn,
            _ => false
        };
        return supported ? null : $"{target}{IsolatedVectorHostApplicationValues.DoesNotImplement}{profile.IndexKind}{IsolatedVectorHostApplicationValues.AlgorithmModeSeparator}{profile.QueryMode}{IsolatedVectorHostApplicationValues.Natively}";
    }

    private static Task WriteAsync(IsolatedHostSettings settings, string status, string? reason,
        ComparisonReport? report, CancellationToken cancellationToken)
        => IsolatedHostReportWriter.WriteAsync(new IsolatedComparisonReport(
            IsolatedComparisonContract.Current.WorkerSchemaVersion, settings.Worker, status, reason, report),
            settings.OutputDirectory, settings.HostExecution, cancellationToken);

    internal static void ValidateNativeVectorReport(ComparisonReport report, IsolatedHostSettings settings)
    {
        var selection = settings.Selection;
        if (selection.VectorProfile is not { } profile || report.Targets.Length != IsolatedVectorHostApplicationValues.SingleElementOffset
            || report.Targets[IsolatedVectorHostApplicationValues.FirstIndex].Name != selection.Target || report.Cases.Length != IsolatedVectorHostApplicationValues.SingleElementOffset
            || report.Cases[IsolatedVectorHostApplicationValues.FirstIndex].Target != selection.Target || report.Cases[IsolatedVectorHostApplicationValues.FirstIndex].VectorMetrics is null
            || report.VectorProfile?.Id != profile.Id || report.Options is not null || report.ScaledProfile is not null
            || report.Targets[IsolatedVectorHostApplicationValues.FirstIndex].Cluster is not { } cluster || cluster.Nodes != selection.NodeCount
            || cluster.DataCopies != selection.NodeCount)
        {
            throw new ComparisonFailureException(IsolatedHostConstants.Failure);
        }
    }

}
