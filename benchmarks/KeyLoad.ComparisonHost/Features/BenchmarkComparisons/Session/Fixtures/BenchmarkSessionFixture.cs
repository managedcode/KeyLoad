using System.Collections.Immutable;
using System.Runtime.InteropServices;
using KeyLoad.Comparisons;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Configuration;
using TUnit.Core.Interfaces;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Owns one native target for all measured tests; setup opens clients without loading the corpus.</summary>
internal sealed class BenchmarkSessionFixture : IAsyncInitializer, IAsyncDisposable
{
    internal const string Measured = "measured";
    internal const string Failed = "failed";
    internal const string Unavailable = "unavailable";
    private const int UnverifiedRecords = 0;
    private const double NoElapsedMeasurement = 0;
    private const string Success = "success";
    private const string Failure = "failure";
    private const string TopologyUnavailable = "NativeTopologyUnavailable";
    private const string CapabilitiesUnavailable = "NativeSessionCapabilitiesUnavailable";
    internal const string LoadCase = "load-million";
    internal const string IndexCase = "index";
    internal const string SequentialReadCase = "sequential-read";
    internal const string RandomReadCase = "random-read";
    internal const string SearchCase = "search";
    internal const string MixedCase = "mixed";
    internal const string VerifyCase = "verify-final";
    private static readonly string[] CaseIds =
        [LoadCase, IndexCase, SequentialReadCase, RandomReadCase, SearchCase, MixedCase, VerifyCase];
    private readonly List<BenchmarkSessionArtifact> artifacts = [];
    private readonly Dictionary<string, string> outcomes = new(StringComparer.Ordinal);
    private IDisposable? configuration;
    private BenchmarkSessionHostSettings? settings;
    private IsolatedHostTargetOwner? owner;
    private BenchmarkSession? session;
    private ImmutableArray<BenchmarkSessionAssembly> assemblies;

    public BenchmarkSessionFixture()
    {
    }

    public async Task InitializeAsync()
    {
        try
        {
            InitializeCore();
        }
        catch (Exception original) when (CqrsRuntimeFailures.FindFatal(original) is null)
        {
            settings = null;
            try
            {
                await ReleaseAsync();
            }
            catch (Exception cleanup) when (CqrsRuntimeFailures.FindFatal(cleanup) is null)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
    }

    private void InitializeCore()
    {
        _ = SerializationExecutionRegistration.Process.Value;
        var input = new ConfigurationManager();
        configuration = input;
        input.AddJsonFile(Path.Combine(AppContext.BaseDirectory, NativeComparisonExecutionRegistration.ConfigurationPath), optional: false);
        input.AddEnvironmentVariables();
        settings = BenchmarkSessionHostSettings.Read(input);
        assemblies = BenchmarkSessionArtifacts.Assemblies();
        if (settings.Native.UnsupportedReason is null)
        {
            owner = new(NativeComparisonExecutionRegistration.Read(input),
                NativeComparisonExecutionRegistration.ReadDiagnostics(input),
                NativeComparisonExecutionRegistration.ReadIsolatedAdmission(input),
                NativeComparisonExecutionRegistration.ReadLifecycle(input),
                NativeComparisonExecutionRegistration.ReadClient(input),
                NativeComparisonExecutionRegistration.ReadTranslation(input), TimeProvider.System);
            var progress = new DocumentHostProgress(owner.ExecutionOptions, owner.Clock);
            session = CreateSession(owner, settings.Native, progress.Observe);
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
            // Native connection parsers may include credentials in their original exception messages.
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
            report = input.Native.UnsupportedReason is { } reason
                ? new(id, Unavailable, reason, UnverifiedRecords, new BenchmarkSessionOptions().Records, UnverifiedRecords, [], NoElapsedMeasurement)
                : await operation(session ?? throw new InvalidOperationException(BenchmarkSessionHostSettings.Profile), token);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            report = new(id, Failed, error.GetType().Name, UnverifiedRecords, new BenchmarkSessionOptions().Records, UnverifiedRecords, [], NoElapsedMeasurement);
            await RetainAsync(report);
            throw;
        }
        await RetainAsync(report);
        await Assert.That(report.CaseId).IsEqualTo(id);
        await Assert.That(report.Status is Measured or Unavailable).IsTrue().Because(report.Detail ?? id);
    }

    private async Task RetainAsync(BenchmarkSessionCaseReport report)
    {
        if (!CaseIds.Contains(report.CaseId, StringComparer.Ordinal) || !outcomes.TryAdd(report.CaseId, report.Status))
        {
            throw new InvalidOperationException(BenchmarkSessionHostSettings.Profile);
        }
        artifacts.Add(await BenchmarkSessionArtifacts.WriteCaseAsync(settings!.Native.OutputDirectory, report, settings.Native.HostExecution));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await RetainStateAsync();
        }
        finally
        {
            await ReleaseAsync();
        }
        GC.SuppressFinalize(this);
    }

    private async Task RetainStateAsync()
    {
        if (settings is not { } input)
        {
            return;
        }
        var worker = input.Native.Worker;
        var unavailable = input.Native.UnsupportedReason;
        var capabilitiesUnavailable = unavailable is null && outcomes.Count == CaseIds.Length
            && outcomes.Values.All(status => status == Unavailable);
        var reason = unavailable is not null ? TopologyUnavailable : capabilitiesUnavailable ? CapabilitiesUnavailable : null;
        var outcome = reason is not null ? Unavailable : outcomes.Values.Any(status => status == Failed) ? Failure : Success;
        var profile = owner?.ObservedProfile;
        if (capabilitiesUnavailable || !outcomes.ContainsValue(Measured)
            && profile is not { Image: not null, Cluster: not null })
        {
            profile = null;
        }
        await BenchmarkSessionArtifacts.WriteStateAsync(input.Native.OutputDirectory,
            new(BenchmarkSessionHostSettings.Profile, input.SessionId, worker.Target, worker.NodeCount,
                worker.SourceRevision, worker.RunId, worker.Attempt, worker.JobId,
                worker.Repository, worker.Ref, worker.Workflow,
                new(unavailable is null ? input.Native.Native?.Image : null, input.Native.Identity.LoadGeneratorImage), profile,
                new(RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
                    Environment.ProcessorCount, RuntimeInformation.FrameworkDescription, input.Native.Storage), assemblies,
                artifacts.ToImmutableArray(), outcome, reason), input.Native.HostExecution);
    }

    private async ValueTask ReleaseAsync()
    {
        var owned = owner;
        owner = null;
        session = null;
        try
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }
        }
        finally
        {
            var lifetime = configuration;
            configuration = null;
            lifetime?.Dispose();
        }
    }
}
