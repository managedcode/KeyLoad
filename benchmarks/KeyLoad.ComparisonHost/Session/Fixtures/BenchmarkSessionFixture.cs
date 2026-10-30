using System.Collections.Immutable;
using System.Runtime.InteropServices;
using KeyLoad.Comparisons;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Configuration;
using TUnit.Core.Interfaces;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Owns one execution target for all measured tests; setup opens clients without loading the corpus.</summary>
internal sealed class BenchmarkSessionFixture : IAsyncInitializer, IAsyncDisposable
{
    internal const string Measured = "measured";
    internal const string Failed = "failed";
    internal const string Unavailable = "unavailable";
    private const double NoElapsedMeasurement = 0;
    private const string Success = "success";
    private const string Failure = "failure";
    private const string TopologyUnavailable = "RuntimeTopologyUnavailable";
    internal const string LoadCase = "load-million";
    internal const string IndexCase = "index";
    internal const string SequentialReadCase = "sequential-read";
    internal const string RandomReadCase = "random-read";
    internal const string SearchCase = "search";
    internal const string MixedCase = "mixed";
    internal const string VerifyCase = "verify-final";
    private static readonly string[] CaseIds =
        [LoadCase, IndexCase, SequentialReadCase, RandomReadCase, SearchCase, MixedCase, VerifyCase,
            BenchmarkSessionModelCases.Prepare, BenchmarkSessionModelCases.Vector, BenchmarkSessionModelCases.Queue, BenchmarkSessionModelCases.Append,
            BenchmarkSessionModelCases.Read, BenchmarkSessionModelCases.Mixed];
    private readonly List<BenchmarkSessionArtifact> artifacts = [];
    private readonly Dictionary<string, string> outcomes = new(StringComparer.Ordinal);
    private IDisposable? configuration;
    private BenchmarkSessionHostSettings? settings;
    private IsolatedHostTargetOwner? owner;
    private BenchmarkSession? session;
    private ImmutableArray<BenchmarkSessionAssembly> assemblies;
    private BenchmarkSessionProcesses? processes;

    public BenchmarkSessionFixture()
    {
    }

    public async Task InitializeAsync()
    {
        try
        {
            InitializeCore();
        }
        catch (Exception original)
        {
            settings = null;
            await ReleaseAsync(Task.FromException(original));
            throw;
        }
    }

    private void InitializeCore()
    {
        var input = new ConfigurationManager();
        configuration = input;
        input.AddJsonFile(Path.Combine(AppContext.BaseDirectory, ComparisonExecutionRegistration.ConfigurationPath), optional: false);
        input.AddEnvironmentVariables();
        settings = BenchmarkSessionHostSettings.Read(input);
        assemblies = BenchmarkSessionArtifacts.Assemblies();
        processes = BenchmarkSessionProcesses.ReadCurrent();
        if (settings.Execution.UnsupportedReason is null)
        {
            owner = new(ComparisonExecutionRegistration.Read(input),
                ComparisonExecutionRegistration.ReadDiagnostics(input),
                ComparisonExecutionRegistration.ReadLifecycle(input),
                ComparisonExecutionRegistration.ReadClient(input),
                ComparisonExecutionRegistration.ReadTranslation(input), TimeProvider.System);
            var progress = new DocumentHostProgress(owner.ExecutionOptions, owner.Clock);
            session = CreateSession(owner, settings.Execution, progress.Observe);
        }
    }

    private static BenchmarkSession CreateSession(IsolatedHostTargetOwner targetOwner, IsolatedHostSettings selected,
        Action<DocumentComparisonProgress> progress)
    {
        try
        {
            return targetOwner.CreateSession(selected, progress);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            // Execution connection parsers may include credentials in their original exception messages.
            throw new ComparisonFailureException(IsolatedHostConstants.Failure);
        }
    }

    internal async Task RunAsync(string id, Func<BenchmarkSession, CancellationToken, Task<BenchmarkSessionCaseReport>> operation,
        CancellationToken token)
    {
        var input = settings ?? throw new InvalidOperationException(BenchmarkSessionHostSettings.Profile);
        BenchmarkSessionCaseReport report;
        try
        {
            report = input.Execution.UnsupportedReason is { } reason
                ? new(id, Unavailable, reason, 0, new BenchmarkSessionOptions().Records, 0, [], NoElapsedMeasurement)
                : await operation(session ?? throw new InvalidOperationException(BenchmarkSessionHostSettings.Profile), token);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            report = new(id, Failed, error.GetType().Name, 0, new BenchmarkSessionOptions().Records, 0, [], NoElapsedMeasurement);
            await BenchmarkSessionFixtureDisposal.RetainFailureAsync(error, RetainAsync(report));
            throw;
        }
        await RetainAsync(report);
        await Assert.That(report.CaseId).IsEqualTo(id);
        if (input.Execution.UnsupportedReason is null && BenchmarkSessionModelCases.Required(input.Execution.Selection.Target, id))
        {
            await Assert.That(report.Status).IsEqualTo(Measured).Because(report.Detail ?? id);
        }
        await Assert.That(report.Status is Measured or Unavailable).IsTrue().Because(report.Detail ?? id);
    }

    private async Task RetainAsync(BenchmarkSessionCaseReport report)
    {
        if (!CaseIds.Contains(report.CaseId, StringComparer.Ordinal) || !outcomes.TryAdd(report.CaseId, report.Status))
        {
            throw new InvalidOperationException(BenchmarkSessionHostSettings.Profile);
        }
        artifacts.Add(await BenchmarkSessionArtifacts.WriteCaseAsync(settings!.Execution.OutputDirectory, report, settings.Execution.HostExecution));
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync(RetainStateAsync());
        GC.SuppressFinalize(this);
    }

    private async Task RetainStateAsync()
    {
        if (settings is not { } input)
        {
            return;
        }
        var worker = input.Execution.Worker;
        var unavailable = input.Execution.UnsupportedReason;
        var reason = unavailable is not null ? TopologyUnavailable : null;
        var outcome = reason is not null ? Unavailable : outcomes.Values.Any(status => status == Failed) ? Failure : Success;
        var profile = owner?.ObservedProfile;
        if (!outcomes.ContainsValue(Measured)
            && profile is not { Cluster: not null })
        {
            profile = null;
        }
        await BenchmarkSessionArtifacts.WriteStateAsync(input.Execution.OutputDirectory,
            new(BenchmarkSessionHostSettings.Profile, input.SessionId, worker.Target, worker.NodeCount,
                worker.SourceRevision, worker.RunId, worker.Attempt, worker.JobId,
                worker.Repository, worker.Ref, worker.Workflow,
                new(unavailable is null ? input.Execution.Execution?.Image : null),
                processes ?? throw new InvalidOperationException(BenchmarkSessionHostSettings.Profile), profile,
                new(RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
                    Environment.ProcessorCount, RuntimeInformation.FrameworkDescription, input.Execution.Storage), assemblies,
                artifacts.ToImmutableArray(), outcome, reason), input.Execution.HostExecution);
    }

    private ValueTask ReleaseAsync(Task original)
    {
        var owned = owner;
        var lifetime = configuration;
        owner = null;
        configuration = null;
        session = null;
        return BenchmarkSessionFixtureDisposal.CompleteAsync(original, owned, lifetime);
    }
}
