using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceEvidenceCollector(
    ComparisonWorkerSelection selection, string output, CancellationToken applicationStopping)
{
    private const string RunnerName = "comparisons";
    private const string BootstrapFragment = "bootstrap";
    private const string SidecarName = "server-resource-evidence.json";
    private const string Schema = "server-resource-evidence.v1";
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _applicationStopping = applicationStopping;
    private readonly ScaleServerResourceEvidenceCompletion _completion = new();
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private ScaleServerResourceSampler[] _samplers = [];
    private ScaleServerHardware? _hardware;
    private ScaleServerEnvelope? _envelope;
    private Task? _observation;

    internal Task StartAsync(IEnumerable<ContainerResource> resources, Func<CancellationToken, Task> readiness, CancellationToken cancellationToken)
    {
        if (_observation is not null)
        {
            throw new InvalidOperationException("Server resource observation already started.");
        }

        var native = resources.Where(resource => resource.Name != RunnerName
            && !resource.Name.Contains(BootstrapFragment, StringComparison.Ordinal))
            .Take(ScaleServerResourceBounds.MaxContainers + 1).ToArray();
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
                .Where(mount => !mount.IsReadOnly).Take(ScaleServerResourceBounds.MaxMounts + 1)
                .Select(mount => mount.Target).ToArray();
            if (mountTargets.Length > ScaleServerResourceBounds.MaxMounts || mountTargets.Length == 0)
            {
                _missing.Add(ScaleServerResourceBounds.StorageMissing);
            }

            samplers.Add(new ScaleServerResourceSampler(annotation.Name,
                mountTargets.Length <= ScaleServerResourceBounds.MaxMounts ? mountTargets : []));
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
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(ScaleServerResourceBounds.MaxObservationMinutes));
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

            (_hardware, _envelope) = await ScaleServerHostEvidence.ReadAsync(token);
            if (_hardware is null)
            {
                _missing.Add(ScaleServerResourceBounds.HardwareMissing);
            }

            if (_envelope is null)
            {
                _missing.Add(ScaleServerResourceBounds.EnvelopeMissing);
            }

            if (_samplers.Length == 0)
            {
                _missing.Add(ScaleServerResourceBounds.SamplingMissing);
            }

            var timer = Stopwatch.StartNew();
            for (var sample = 0; sample < ScaleServerResourceBounds.MaxSamples; sample++)
            {
                token.ThrowIfCancellationRequested();
                var budget = new ScaleServerResourceSampleBudget(ScaleServerResourceBounds.MaxSampleMetadataBytes);
                foreach (var sampler in _samplers)
                {
                    await sampler.SampleAsync(budget, token);
                    if (sampler.StorageUnavailable)
                    {
                        _missing.Add(ScaleServerResourceBounds.StorageMissing);
                    }
                }
                if (timer.Elapsed >= TimeSpan.FromMinutes(ScaleServerResourceBounds.MaxObservationMinutes))
                {
                    _missing.Add(ScaleServerResourceBounds.SamplingMissing);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(ScaleServerResourceBounds.CadenceSeconds), token);
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
        var workerPath = Path.Combine(output, "worker.json");
        if (!File.Exists(workerPath))
        {
            _missing.Add(ScaleServerResourceBounds.SamplingMissing);
            return;
        }
        if (new FileInfo(workerPath).Length > ScaleServerResourceBounds.MaxWorkerBytes)
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

        if (records.Any(record => record.WritableMounts.Length == 0) || _samplers.Any(sampler => sampler.StorageUnavailable))
        {
            _missing.Add(ScaleServerResourceBounds.StorageMissing);
        }

        var document = new ScaleServerResourceEvidence(Schema, Environment.GetEnvironmentVariable("GITHUB_SHA") ?? string.Empty,
            Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? string.Empty,
            Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? string.Empty,
            Environment.GetEnvironmentVariable("KEYLOAD_COMPARISON_JOB_ID") ?? string.Empty,
            selection.Target, selection.NodeCount, selection.Scenario.ToString(), selection.Profile, workerHash,
            _hardware, _envelope, records, [.. _missing.Order(StringComparer.Ordinal)], _missing.Count == 0);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (bytes.Length > ScaleServerResourceBounds.MaxSidecarBytes)
        {
            throw new InvalidDataException("Server resource evidence exceeded its bound.");
        }

        Directory.CreateDirectory(output);
        await using var stream = new FileStream(Path.Combine(output, SidecarName), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[4096];
        long total = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token);
            if (count == 0)
            {
                break;
            }

            if (total > ScaleServerResourceBounds.MaxWorkerBytes - count)
            {
                throw new InvalidDataException("The isolated worker report exceeded its bound.");
            }

            total += count;
            hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
