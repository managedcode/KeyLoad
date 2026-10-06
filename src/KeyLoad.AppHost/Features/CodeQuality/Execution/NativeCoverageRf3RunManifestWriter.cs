using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal static class NativeCoverageRf3RunManifestWriter
{
    private const string DurationFormat = "c";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<NativeCoverageRf3Run> WriteAsync(string resultsDirectory,
        NativeCoverageRf3Admission admission, IOptions<NativeCoverageExecutionOptions> coverageOptions,
        IOptions<TestExecutionOptions> testOptions, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsDirectory);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(coverageOptions);
        ArgumentNullException.ThrowIfNull(testOptions);
        var coverage = coverageOptions.Value;
        var tests = testOptions.Value;
        if (!coverage.IsValid() || !tests.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        Directory.CreateDirectory(resultsDirectory);
        var runId = Guid.NewGuid().ToString(NativeCoverageRf3Protocol.GuidFormat);
        var path = Path.Combine(resultsDirectory, NativeCoverageRf3Protocol.RunManifestName);
        var document = CreateDocument(runId, admission, coverage, tests);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        if (bytes.Length > NativeCoverageRf3Protocol.MaximumRunManifestBytes)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidRun);
        }
        await WriteCreateOnlyAsync(path, bytes, coverage.ReadBufferBytes, cancellationToken).ConfigureAwait(false);
        return new(runId, path, admission, NativeCoverageRf3Protocol.CoverageImagePrefix +
            Guid.ParseExact(runId, NativeCoverageRf3Protocol.GuidFormat).ToString(NativeCoverageRf3Protocol.GuidCompactFormat));
    }

    private static object CreateDocument(string runId, NativeCoverageRf3Admission admission,
        NativeCoverageExecutionOptions coverage, TestExecutionOptions tests)
        => new
        {
            schemaVersion = NativeCoverageRf3Protocol.RunSchemaVersion,
            runId,
            suite = NativeCoverageRf3Protocol.Rf3Suite,
            sourceRevision = admission.SourceRevision,
            sourceManifestSha256 = admission.SourceManifestSha256,
            testImageManifestSha256 = admission.TestImageManifestSha256,
            filter = NativeCoverageRf3Protocol.Filter,
            caseIdentities = admission.Contributors.OrderBy(item => item.ClassName, StringComparer.Ordinal)
                .ThenBy(item => item.MethodName, StringComparer.Ordinal)
                .ThenBy(item => item.InstanceName, StringComparer.Ordinal)
                .Select(item => new { item.ClassName, item.MethodName, item.InstanceName }),
            executionPolicy = new
            {
                coverage = CreateCoveragePolicy(coverage),
                tests = new
                {
                    ordinaryTimeout = tests.OrdinaryTimeout.ToString(DurationFormat),
                    clusterTimeout = tests.ClusterTimeout.ToString(DurationFormat),
                    intensiveTimeout = tests.IntensiveTimeout.ToString(DurationFormat),
                    nativeControlTimeout = tests.NativeControlTimeout.ToString(DurationFormat),
                    nativeScaledTimeout = tests.NativeScaledTimeout.ToString(DurationFormat),
                    nativeVectorTimeout = tests.NativeVectorTimeout.ToString(DurationFormat),
                    applicationCleanupTimeout = tests.ApplicationCleanupTimeout.ToString(DurationFormat),
                    imageCleanupTimeout = tests.ImageCleanupTimeout.ToString(DurationFormat),
                    terminationGrace = tests.TerminationGrace.ToString(DurationFormat),
                    processSettlementTimeout = tests.ProcessSettlementTimeout.ToString(DurationFormat),
                    processExitPollInterval = tests.ProcessExitPollInterval.ToString(DurationFormat),
                    tests.CleanupOutputCharacters,
                    tests.MaximumFilterCharacters,
                    tests.MaximumPathCharacters
                }
            }
        };

    private static object CreateCoveragePolicy(NativeCoverageExecutionOptions coverage)
        => new
        {
            coverage.MaximumDescriptorBytes,
            coverage.MaximumFiles,
            coverage.ReadBufferBytes,
            coverage.MaximumTotalBytes,
            coverage.MaximumFileBytes,
            coverage.MaximumPathCharacters,
            coverage.MaximumManifestBytes,
            coverage.MaximumReportBytes,
            shutdownTimeout = coverage.ShutdownTimeout.ToString(DurationFormat),
            settlementTimeout = coverage.SettlementTimeout.ToString(DurationFormat),
            containerStopTimeout = coverage.ContainerStopTimeout.ToString(DurationFormat),
            applicationCleanupTimeout = coverage.ApplicationCleanupTimeout.ToString(DurationFormat)
        };

    private static async Task WriteCreateOnlyAsync(string path, byte[] bytes, int bufferBytes, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        FlushToDisk(stream);
    }

    private static void FlushToDisk(FileStream stream) => stream.Flush(flushToDisk: true);
}

internal sealed record NativeCoverageRf3Run(string RunId, string ManifestPath, NativeCoverageRf3Admission Admission,
    string ImageReference);
