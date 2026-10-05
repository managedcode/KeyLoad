using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceEvidenceCollector(
    ComparisonWorkerSelection selection, string output, CancellationToken applicationStopping,
    IOptions<ScaleServerResourceOptions> executionOptions, IOptions<BenchmarkProvenanceOptions> provenanceOptions)
{
    private const string RunnerName = "comparisons";
    private const string BootstrapFragment = "bootstrap";
    private const string SidecarName = "server-resource-evidence.json";
    private const string Schema = "server-resource-evidence.v2";
    private readonly ScaleServerResourceOptions _settings = executionOptions.Value;
    private readonly BenchmarkProvenanceOptions _provenance = provenanceOptions.Value;
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
        const int EmptyValue = 0;
        const int SampleInitialValue = 0;

        using var deadline = new CancellationTokenSource(_settings.MaximumObservation);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            applicationToken, _applicationStopping, _stop.Token, deadline.Token);
        var token = linked.Token;
        try
        {
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

    private bool IsNormalOwnerSettlement(OperationCanceledException failure, CancellationToken observationToken,
        CancellationToken applicationToken, CancellationToken deadlineToken)
        => _stop.IsCancellationRequested && !applicationToken.IsCancellationRequested
            && !_applicationStopping.IsCancellationRequested && !deadlineToken.IsCancellationRequested
            && failure.CancellationToken == observationToken;

    private async Task WriteAsync(CancellationToken token)
    {
        const string Path2Text = "worker.json";
        const int EmptyValue = 0;
        const string MessageText = "Server resource evidence exceeded its bound.";

        var workerPath = Path.Combine(output, Path2Text);
        if (!File.Exists(workerPath))
        {
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
            return;
        }
        if (new FileInfo(workerPath).Length > _settings.MaxWorkerBytes)
        {
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
            return;
        }
        var workerHash = await HashAsync(workerPath, token);
        var records = _samplers.Select(sampler => sampler.ToRecord()).Where(record => record is not null)
            .Select(record => record!).ToArray();
        if (records.Length != selection.NodeCount || _samplers.Any(sampler => sampler.Invalidated))
        {
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
        }

        if (records.Any(record => record.SampleCount < ScaleServerResourceBounds.MinimumSamples))
        {
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
        }

        if (records.Any(record => record.WritableMounts.Length == EmptyValue) || _samplers.Any(sampler => sampler.StorageUnavailable))
        {
            _missing.Add(ScaleServerResourceBounds.StorageMissing);
        }

        var document = new ScaleServerResourceEvidence(Schema, _provenance.SourceRevision,
            _provenance.WorkflowRunId,
            _provenance.RunAttempt,
            _provenance.JobId,
            selection.Target, selection.NodeCount, selection.Scenario.ToString(), selection.Profile, workerHash,
            _hardware, _envelope, records, [.. _missing.Order(StringComparer.Ordinal)], _missing.Count == EmptyValue, ScaleServerObservationPolicySnapshot.Capture(executionOptions));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (bytes.Length > _settings.MaxSidecarBytes)
        {
            throw new InvalidDataException(MessageText);
        }

        Directory.CreateDirectory(output);
        await using var stream = new FileStream(Path.Combine(output, SidecarName), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, _settings.NativeReadBufferBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private async Task<string> HashAsync(string path, CancellationToken token)
    {
        const int TotalInitialValue = 0;
        const int EmptyValue = 0;
        const string MessageText = "The isolated worker report exceeded its bound.";
        const int OffsetValue = 0;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            _settings.NativeReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[_settings.NativeReadBufferBytes];
        long total = TotalInitialValue;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token);
            if (count == EmptyValue)
            {
                break;
            }

            if (total > _settings.MaxWorkerBytes - count)
            {
                throw new InvalidDataException(MessageText);
            }

            total += count;
            hash.AppendData(buffer, OffsetValue, count);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
