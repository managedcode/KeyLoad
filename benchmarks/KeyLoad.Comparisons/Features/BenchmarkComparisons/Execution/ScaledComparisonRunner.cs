using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace KeyLoad.Comparisons;

internal sealed class ScaledComparisonRunner(ScaledComparisonProfile profile, IOptions<NativeComparisonExecutionOptions> executionOptions, Action<string>? progress)
{
    private const int RequiredLatencySampleCount = 4_096;
    private static readonly Scenario[] Scenarios = [Scenario.PointRead, Scenario.DocumentWrite, Scenario.DocumentUpdate, Scenario.DocumentDelete];

    internal async Task<ComparisonReport> RunAsync(IComparisonTarget[] targets, string? sourceRevision,
        string storage, Scenario? selectedScenario, CancellationToken cancellationToken)
    {
        ScaledComparisonRunnerValidation.Validate(targets, selectedScenario);
        var started = TimeProvider.System.GetUtcNow();
        var corpus = new ScaledComparisonCorpus(profile);
        var cases = await RunTargetsAsync(targets, corpus, selectedScenario, cancellationToken).ConfigureAwait(false);
        return new ComparisonReport(3, Guid.NewGuid(), started, null, corpus.Sha256,
            "closed-loop scaled S1; bounded native readback before timed operations",
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
            if (supported.Length == 0 || selected.All(scenario => !target.Supports(scenario)))
            {
                cases.AddRange(selected.Select(scenario => new ComparisonCase(target.Profile.Name, scenario, 0,
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
                cases.Add(await ScaledComparisonCaseRunner.RunAsync(target, corpus, scenario, failure,
                    progress, cancellationToken, executionOptions).ConfigureAwait(false));
            }
        }
        return cases;
    }

    private static ComparisonCase Cancelled(IComparisonTarget target, Scenario scenario, ScaledComparisonProfile profile)
    {
        var accounting = new ScaledOperationAccounting(profile.Operations, 0, 0, 0, 0, 0,
            profile.Operations, "evenly-spaced-operation-indices.v1", RequiredLatencySampleCount, 0, RequiredLatencySampleCount);
        return new(target.Profile.Name, scenario, 0, ComparisonStatuses.Failed, "Cancelled before this case started.", null, [])
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
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Scaled native comparison requires Linux.");
        }
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Length == 0 || targets.Any(target => target is null))
        {
            throw new ArgumentException("At least one non-null target is required.", nameof(targets));
        }
        if (scenario is { } selected && selected is not (Scenario.PointRead or Scenario.DocumentWrite
            or Scenario.DocumentUpdate or Scenario.DocumentDelete))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario), "The S1 profile permits point read and document CRUD only.");
        }
    }
}
