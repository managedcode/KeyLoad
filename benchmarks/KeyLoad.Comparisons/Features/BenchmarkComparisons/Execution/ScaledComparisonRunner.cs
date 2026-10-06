using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace KeyLoad.Comparisons;

internal sealed class ScaledComparisonRunner(ScaledComparisonProfile profile, IOptions<NativeComparisonExecutionOptions> executionOptions, Action<string>? progress)
{
    private const int EmptyCount = 0;

    private const int RequiredLatencySampleCount = 4_096;
    private static readonly Scenario[] Scenarios = [Scenario.PointRead, Scenario.DocumentWrite, Scenario.DocumentUpdate, Scenario.DocumentDelete];

    internal async Task<ComparisonReport> RunAsync(IComparisonTarget[] targets, string? sourceRevision,
        string storage, Scenario? selectedScenario, CancellationToken cancellationToken)
    {
        const int ThirdContractOrdinal = 3;
        const string ClosedLoopScaledS1BoundedNativeReadbackBeforeTimedOperationsContractText = "closed-loop scaled S1; bounded native readback before timed operations";

        ScaledComparisonRunnerValidation.Validate(targets, selectedScenario);
        var started = TimeProvider.System.GetUtcNow();
        var corpus = new ScaledComparisonCorpus(profile);
        var cases = await RunTargetsAsync(targets, corpus, selectedScenario, cancellationToken).ConfigureAwait(false);
        return new ComparisonReport(ThirdContractOrdinal, Guid.NewGuid(), started, null, corpus.Sha256,
            ClosedLoopScaledS1BoundedNativeReadbackBeforeTimedOperationsContractText,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            RuntimeInformation.FrameworkDescription, storage, sourceRevision,
            targets.Select(target => target.Profile).ToImmutableArray(), cases.ToImmutableArray())
        {
            ScaledProfile = profile
        };
    }

    private async Task<List<ComparisonCase>> RunTargetsAsync(IComparisonTarget[] targets, IComparisonCorpus corpus,
        Scenario? selectedScenario, CancellationToken cancellationToken)
    {
        const int NoItems = 0;
        const int NoObservedItems = 0;

        var cases = new List<ComparisonCase>();
        foreach (var target in targets)
        {
            var supported = Scenarios.Where(target.Supports).ToArray();
            var selected = selectedScenario is { } requested ? new[] { requested } : Scenarios;
            if (cancellationToken.IsCancellationRequested)
            {
                cases.AddRange(selected.Select(scenario => Cancelled(target, scenario, profile)));
                continue;
            }
            if (supported.Length == NoItems || selected.All(scenario => !target.Supports(scenario)))
            {
                cases.AddRange(selected.Select(scenario => new ComparisonCase(target.Profile.Name, scenario, NoObservedItems,
                    ComparisonStatuses.Unsupported, target.UnsupportedReason, null, [])));
                continue;
            }
            string? failure;
            try
            {
                failure = await InitializeAndVerifyAsync(target, corpus, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cases.AddRange(selected.Select(scenario => Cancelled(target, scenario, profile)));
                continue;
            }
            foreach (var scenario in selected)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cases.Add(Cancelled(target, scenario, profile));
                    continue;
                }
                cases.Add(await ScaledComparisonCaseRunner.RunAsync(target: target, corpus: corpus, scenario: scenario,
                    setupFailure: failure, progress: progress, cancellationToken: cancellationToken, executionOptions: executionOptions).ConfigureAwait(false));
            }
        }
        return cases;
    }

    private static ComparisonCase Cancelled(IComparisonTarget target, Scenario scenario, ScaledComparisonProfile profile)
    {
        const int NoObservedItems = 0;
        const string EvenlySpacedOperationIndicesV1Token = "evenly-spaced-operation-indices.v1";
        const string CancelledBeforeThisCaseStartedDetail = "Cancelled before this case started.";

        var accounting = new ScaledOperationAccounting(profile.Operations, NoObservedItems, NoObservedItems, NoObservedItems, EmptyCount, NoObservedItems,
            profile.Operations, EvenlySpacedOperationIndicesV1Token, RequiredLatencySampleCount, NoObservedItems, RequiredLatencySampleCount);
        return new(target.Profile.Name, scenario, NoObservedItems, ComparisonStatuses.Failed, CancelledBeforeThisCaseStartedDetail, null, [])
        {
            Scaled = accounting
        };
    }

    private static async Task<string?> InitializeAndVerifyAsync(IComparisonTarget target, IComparisonCorpus corpus,
        CancellationToken cancellationToken)
    {
        try
        {
            await target.InitializeAsync(corpus, cancellationToken).ConfigureAwait(false);
            await using var session = await target.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
            await ScaledCorpusReadbackVerifier.VerifyAsync(session, corpus, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            return ComparisonErrors.Safe(error);
        }
    }
}

internal static class ScaledComparisonRunnerValidation
{
    internal static void Validate(IComparisonTarget[] targets, Scenario? scenario)
    {
        const string ScaledNativeComparisonRequiresLinuxDetail = "Scaled native comparison requires Linux.";
        const int NoItems = 0;
        const string AtLeastOneNonNullTargetIsRequiredDetail = "At least one non-null target is required.";
        const string ValidateMessageText = "The S1 profile permits point read and document CRUD only.";

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(ScaledNativeComparisonRequiresLinuxDetail);
        }
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Length == NoItems || targets.Any(target => target is null))
        {
            throw new ArgumentException(AtLeastOneNonNullTargetIsRequiredDetail, nameof(targets));
        }
        if (scenario is { } selected && selected is not (Scenario.PointRead or Scenario.DocumentWrite
            or Scenario.DocumentUpdate or Scenario.DocumentDelete))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), ValidateMessageText);
        }
    }
}
