using System.Diagnostics;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceEvidenceCollector(
    ComparisonWorkerSelection selection, string output,
    IOptions<ScaleServerResourceOptions> executionOptions, IOptions<BenchmarkProvenanceOptions> provenanceOptions,
    CancellationToken applicationStopping, OpenLoopResourceSelection? openLoop = null) : IAsyncDisposable
{
    private const string RunnerName = "comparisons";
    private const string BootstrapFragment = "bootstrap";
    private readonly ScaleServerResourceOptions _settings = executionOptions.Value;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _applicationStopping = applicationStopping;
    private readonly ScaleServerResourceEvidenceCompletion _completion = new(executionOptions);
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private ScaleServerResourceSampler[] _samplers = [];
    private ScaleServerHardware? _hardware;
    private ScaleServerEnvelope? _envelope;
    private Task? _observation;

    internal Task StartAsync(IEnumerable<ContainerResource> resources, Func<CancellationToken, Task> readiness, CancellationToken cancellationToken)
    {
        const string MessageText = "Server resource observation already started.";
        const int Step = 1;
        const int EmptyValue = 0;

        if (_observation is not null)
        {
            throw new InvalidOperationException(MessageText);
        }

        var native = resources.Where(resource => resource.Name != RunnerName
            && !resource.Name.Contains(BootstrapFragment, StringComparison.Ordinal))
            .Take(ScaleServerResourceBounds.MaxContainers + Step).ToArray();
        if (native.Length > ScaleServerResourceBounds.MaxContainers || native.Length != selection.NodeCount)
        {
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
        }

        var samplers = new List<ScaleServerResourceSampler>();
        foreach (var resource in native.Take(ScaleServerResourceBounds.MaxContainers))
        {
            var annotation = resource.Annotations.OfType<ContainerNameAnnotation>().SingleOrDefault();
            if (annotation is null)
            {
                _missing.Add(ScaleServerResourceBounds.SamplingMissing);
                continue;
            }
            var mountTargets = resource.Annotations.OfType<ContainerMountAnnotation>()
                .Where(mount => !mount.IsReadOnly).Take(_settings.MaxMounts + Step)
                .Select(mount => mount.Target).ToArray();
            if (mountTargets.Length > _settings.MaxMounts || mountTargets.Length == EmptyValue)
            {
                _missing.Add(ScaleServerResourceBounds.StorageMissing);
            }

            samplers.Add(new ScaleServerResourceSampler(annotation.Name,
                mountTargets.Length <= _settings.MaxMounts ? mountTargets : []));
        }
        _samplers = samplers.ToArray();
        _observation = ObserveAsync(readiness, cancellationToken);
        return _observation;
    }

    internal Task CompleteAsync(Task observation)
        => _completion.CompleteAsync(observation, _observation, _stop.CancelAsync,
            () => _missing.Add(ScaleServerResourceBounds.SamplingMissing),
            _stop.Dispose, () => WriteAsync(CancellationToken.None));

    private async Task ObserveAsync(Func<CancellationToken, Task> readiness, CancellationToken applicationToken)
    {
        using var deadline = new CancellationTokenSource(_settings.MaximumObservation);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            applicationToken, _applicationStopping, _stop.Token, deadline.Token);
        var token = linked.Token;
        try
        {
            await ObserveOwnedAsync(readiness, token);
        }
        catch (OperationCanceledException failure)
        {
            if (!IsNormalOwnerSettlement(failure, token, applicationToken, deadline.Token))
            {
                _missing.Add(ScaleServerResourceBounds.SamplingMissing);
            }
        }
        catch (IOException) { _missing.Add(ScaleServerResourceBounds.SamplingMissing); }
        catch (UnauthorizedAccessException) { _missing.Add(ScaleServerResourceBounds.SamplingMissing); }
        catch (FormatException) { _missing.Add(ScaleServerResourceBounds.SamplingMissing); }
        catch (InvalidDataException) { _missing.Add(ScaleServerResourceBounds.SamplingMissing); }
        catch (ArgumentException) { _missing.Add(ScaleServerResourceBounds.SamplingMissing); }
        catch (System.Security.SecurityException) { _missing.Add(ScaleServerResourceBounds.SamplingMissing); }
        catch (Aspire.Hosting.DistributedApplicationException) { _missing.Add(ScaleServerResourceBounds.SamplingMissing); }
        catch (System.ComponentModel.Win32Exception) { _missing.Add(ScaleServerResourceBounds.SamplingMissing); }
        catch (OverflowException) { _missing.Add(ScaleServerResourceBounds.SamplingMissing); }
    }

    private async Task ObserveOwnedAsync(Func<CancellationToken, Task> readiness, CancellationToken token)
    {
        const int EmptyValue = 0;
        if (!OperatingSystem.IsLinux())
        {
            _missing.Add(ScaleServerResourceBounds.HardwareMissing);
            _missing.Add(ScaleServerResourceBounds.EnvelopeMissing);
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
            return;
        }
        await readiness(token);
        foreach (var sampler in _samplers)
        {
            sampler.MarkReadyBoundary();
        }

        (_hardware, _envelope) = await ScaleServerHostEvidence.ReadAsync(executionOptions, provenanceOptions, token);
        if (_hardware is null)
        {
            _missing.Add(ScaleServerResourceBounds.HardwareMissing);
        }

        if (_envelope is null)
        {
            _missing.Add(ScaleServerResourceBounds.EnvelopeMissing);
        }

        if (_samplers.Length == EmptyValue)
        {
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
        }

        await SampleAsync(token);
    }

    private async Task SampleAsync(CancellationToken token)
    {
        const int SampleInitialValue = 0;
        var timer = Stopwatch.StartNew();
        for (var sample = SampleInitialValue; sample < _settings.MaxSamples; sample++)
        {
            token.ThrowIfCancellationRequested();
            var budget = new ScaleServerResourceSampleBudget(executionOptions, provenanceOptions);
            foreach (var sampler in _samplers)
            {
                await sampler.SampleAsync(budget, token);
                if (sampler.StorageUnavailable)
                {
                    _missing.Add(ScaleServerResourceBounds.StorageMissing);
                }
            }
            if (timer.Elapsed >= _settings.MaximumObservation)
            {
                _missing.Add(ScaleServerResourceBounds.SamplingMissing);
                return;
            }
            await Task.Delay(_settings.Cadence, token);
        }
        _missing.Add(ScaleServerResourceBounds.SamplingMissing);
    }

    public async ValueTask DisposeAsync()
    {
        if (_observation is { } observation)
        {
            await CompleteAsync(observation);
        }
        else
        {
            _stop.Dispose();
        }
    }

    private bool IsNormalOwnerSettlement(OperationCanceledException failure, CancellationToken observationToken,
        CancellationToken applicationToken, CancellationToken deadlineToken)
        => _stop.IsCancellationRequested && !applicationToken.IsCancellationRequested
            && !_applicationStopping.IsCancellationRequested && !deadlineToken.IsCancellationRequested
            && failure.CancellationToken == observationToken;

    private Task WriteAsync(CancellationToken token)
    {
        var observations = CaptureSnapshot();
        var unsupported = IsolatedComparisonContract.Current.UnsupportedTopologies.Any(item =>
            item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount));
        if (openLoop is not null && !unsupported)
        {
            return OpenLoopResourceEvidenceWriter.WriteAsync(selection, openLoop, output, observations,
                executionOptions, provenanceOptions, token);
        }
        return ScaleServerResourceEvidenceWriter.WriteAsync(selection, output, observations,
            executionOptions, provenanceOptions, token);
    }

    private ScaleServerObservationSnapshot CaptureSnapshot()
    {
        const int EmptyValue = 0;
        var records = _samplers.Select(sampler => sampler.ToRecord()).Where(record => record is not null)
            .Select(record => record!).ToArray();
        if (records.Length != selection.NodeCount || _samplers.Any(sampler => sampler.Invalidated)
            || records.Any(record => record.SampleCount < ScaleServerResourceBounds.MinimumSamples))
        {
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
        }
        if (records.Any(record => record.WritableMounts.Length == EmptyValue)
            || _samplers.Any(sampler => sampler.StorageUnavailable))
        {
            _missing.Add(ScaleServerResourceBounds.StorageMissing);
        }
        return new(_hardware, _envelope, records, [.. _missing.Order(StringComparer.Ordinal)],
            _missing.Count == EmptyValue, ScaleServerObservationPolicySnapshot.Capture(executionOptions));
    }
}
