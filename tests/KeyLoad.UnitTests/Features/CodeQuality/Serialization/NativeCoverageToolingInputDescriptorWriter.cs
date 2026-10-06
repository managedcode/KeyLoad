using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageToolingInputDescriptorWriter
{
    private const int InputCount = 3;
    private const int SchemaVersion = 1;
    private const string SchemaVersionProperty = "schemaVersion";
    private const string InputsProperty = "inputs";
    private const string PathProperty = "path";
    private const string LengthProperty = "length";
    private const string Sha256Property = "sha256";

    internal static async Task WriteAsync(string descriptorPath, string evidenceRoot,
        IReadOnlyList<string> reports, NativeCoverageExecutionOptions options, CancellationToken cancellationToken)
    {
        if (reports.Count != InputCount || File.Exists(descriptorPath) || Directory.Exists(descriptorPath))
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        var descriptor = Serialize(evidenceRoot, reports, options);
        if (descriptor.Length > options.MaximumDescriptorBytes || InputCount + 1 > options.MaximumFiles)
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        await using var stream = new FileStream(descriptorPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            options.ReadBufferBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(descriptor, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        FlushToDisk(stream);
    }

    private static void FlushToDisk(FileStream stream) => stream.Flush(flushToDisk: true);

    private static byte[] Serialize(string evidenceRoot, IReadOnlyList<string> reports, NativeCoverageExecutionOptions options)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber(SchemaVersionProperty, SchemaVersion);
            writer.WriteStartArray(InputsProperty);
            WriteInputs(writer, evidenceRoot, reports, options);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        return buffer.ToArray();
    }

    private static void WriteInputs(Utf8JsonWriter writer, string evidenceRoot,
        IReadOnlyList<string> reports, NativeCoverageExecutionOptions options)
    {
        var totalBytes = 0L;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var report in reports)
        {
            var fullPath = Path.GetFullPath(report);
            var relativePath = Path.GetRelativePath(evidenceRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
                relativePath.StartsWith("../", StringComparison.Ordinal) || relativePath.Length > options.MaximumPathCharacters ||
                !seen.Add(relativePath))
            { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length <= 0 || info.Length > options.MaximumReportBytes || info.LinkTarget is not null ||
                totalBytes > options.MaximumTotalBytes - info.Length)
            { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
            totalBytes += info.Length;
            writer.WriteStartObject();
            writer.WriteString(PathProperty, relativePath);
            writer.WriteNumber(LengthProperty, info.Length);
            writer.WriteString(Sha256Property, NativeCoverageMergeProcess.HashFile(fullPath, options));
            writer.WriteEndObject();
        }
    }
}
