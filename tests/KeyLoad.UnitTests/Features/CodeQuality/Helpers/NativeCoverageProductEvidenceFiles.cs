using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageProductEvidenceFiles
{
    private const string ModifiedTrxName = "rejected-unit-functional-01.trx";
    private const string ModifiedDescriptorName = "rejected-product-descriptor.json";
    internal const int RejectedOwnedEntryCount = 3;

    internal sealed record OriginalFile(string Path, long Length, DateTime LastWriteTimeUtc, string Sha256);

    internal static IReadOnlyDictionary<string, OriginalFile> CaptureOriginalFiles(string evidenceRoot,
        NativeCoverageExecutionOptions limits, out int entryCount)
    {
        var inventory = NativeCoverageMergeEvidenceInventory.Read(evidenceRoot, limits, out entryCount);
        var originals = new Dictionary<string, OriginalFile>(StringComparer.Ordinal);
        foreach (var file in inventory)
        { originals.Add(Path.GetFullPath(file.SourcePath), CaptureFile(file.SourcePath, limits)); }
        return originals;
    }

    internal static void AssertOriginalFiles(IReadOnlyDictionary<string, OriginalFile> originals,
        NativeCoverageExecutionOptions limits)
    {
        foreach (var original in originals.Values)
        {
            var current = CaptureFile(original.Path, limits);
            if (current.Length != original.Length || current.LastWriteTimeUtc != original.LastWriteTimeUtc
                || !string.Equals(current.Sha256, original.Sha256, StringComparison.Ordinal))
            { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        }
    }
    internal static string SelectOwnedDirectory(string evidenceRoot)
    {
        var root = Path.GetFullPath(evidenceRoot);
        var parent = new DirectoryInfo(root);
        if (!parent.Exists || IsReparsePoint(parent))
        { throw new IOException(NativeCoverageMergeProcess.OutputFailure); }
        var path = Path.Combine(root, "unit-admission-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path) || File.Exists(path))
        { throw new IOException(NativeCoverageMergeProcess.OutputFailure); }
        return path;
    }
    internal static void CreateOwnedDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || IsReparsePoint(directory))
        { throw new IOException(NativeCoverageMergeProcess.OutputFailure); }

    }
    internal static void DeleteOwnedDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        { return; }
        if (IsReparsePoint(directory))
        { throw new IOException(NativeCoverageMergeProcess.OutputFailure); }
        var files = Directory.GetFiles(path);
        if (Directory.GetDirectories(path).Length != 0 || files.Any(file =>
                Path.GetFileName(file) is not (ModifiedTrxName or ModifiedDescriptorName)
                || IsReparsePoint(new FileInfo(file))))
        { throw new IOException(NativeCoverageMergeProcess.OutputFailure); }
        foreach (var file in files)
        { File.Delete(file); }
        Directory.Delete(path);
    }
    internal static string WriteRejectedCopies(string evidenceRoot, string descriptorPath,
        string temporaryRoot, IReadOnlyDictionary<string, OriginalFile> originals,
        NativeCoverageExecutionOptions limits)
    {
        var descriptorBytes = ReadBoundedBytes(descriptorPath, limits.MaximumDescriptorBytes, limits.ReadBufferBytes);
        var descriptor = JsonNode.Parse(descriptorBytes)
            ?? throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure);
        var sourceReference = GetUnitGroupTrxReference(descriptor);
        var sourceTrx = ResolveEvidenceReference(evidenceRoot, (string?)sourceReference[NativeCoverageProductFields.Path]);
        var rejectedTrx = ChangeOneOutcomeToFailure(ReadBoundedBytes(sourceTrx,
            limits.MaximumReportBytes, limits.ReadBufferBytes));
        var rejectedDescriptor = JsonNode.Parse(descriptorBytes)
            ?? throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure);
        var rejectedReference = GetUnitGroupTrxReference(rejectedDescriptor);
        var trxPath = Path.Combine(temporaryRoot, ModifiedTrxName);
        var relativeTrx = Path.GetRelativePath(evidenceRoot, trxPath).Replace(Path.DirectorySeparatorChar, '/');
        rejectedReference[NativeCoverageProductFields.Path] = relativeTrx;
        rejectedReference[NativeCoverageProductFields.Length] = rejectedTrx.LongLength;
        rejectedReference[NativeCoverageProductFields.Sha256] = Convert.ToHexStringLower(SHA256.HashData(rejectedTrx));
        var rejectedDescriptorBytes = System.Text.Encoding.UTF8.GetBytes(rejectedDescriptor.ToJsonString());
        var originalBytes = SumBytes(originals);
        if (rejectedTrx.LongLength > limits.MaximumReportBytes || rejectedTrx.LongLength > limits.MaximumFileBytes
            || rejectedDescriptorBytes.LongLength > limits.MaximumDescriptorBytes
            || rejectedDescriptorBytes.LongLength > limits.MaximumFileBytes
            || originalBytes > limits.MaximumTotalBytes - rejectedTrx.LongLength - rejectedDescriptorBytes.LongLength)
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        WriteCreateOnly(trxPath, rejectedTrx);
        var rejectedDescriptorPath = WriteDescriptorCopy(temporaryRoot, rejectedDescriptorBytes);
        _ = NativeCoverageMergeEvidenceInventory.Read(evidenceRoot, limits, out _);
        return rejectedDescriptorPath;
    }
    private static JsonNode GetUnitGroupTrxReference(JsonNode descriptor)
    {
        var runs = descriptor[NativeCoverageProductFields.SuiteRuns]?.AsArray()
            ?? throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure);
        var run = runs.SingleOrDefault(item => string.Equals((string?)item?[NativeCoverageProductFields.Suite],
            "unit-functional-01", StringComparison.Ordinal))
            ?? throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure);
        return run[NativeCoverageProductFields.Trx] ?? throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure);
    }
    private static string WriteDescriptorCopy(string directory, byte[] contents)
    {
        var path = Path.Combine(directory, ModifiedDescriptorName);
        WriteCreateOnly(path, contents);
        return path;
    }
    private static byte[] ChangeOneOutcomeToFailure(byte[] original)
    {
        using var input = new MemoryStream(original, writable: false);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = new XmlDocument { XmlResolver = null };
        document.Load(reader);
        var results = document.GetElementsByTagName("UnitTestResult");
        if (results.Count == 0 || results[0] is not XmlElement result
            || !string.Equals(result.GetAttribute("outcome"), "Passed", StringComparison.Ordinal))
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        result.SetAttribute("outcome", "Failed");
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
    private static string ResolveEvidenceReference(string evidenceRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceRoot));
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        return path;
    }
    private static byte[] ReadBoundedBytes(string path, long maximumBytes, int readBufferBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes || IsReparsePoint(info))
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        var beforeLength = info.Length;
        var beforeWrite = info.LastWriteTimeUtc;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new MemoryStream();
        var buffer = new byte[readBufferBytes];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length > maximumBytes - read)
            { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
            output.Write(buffer, 0, read);
        }
        info.Refresh();
        if (output.Length != beforeLength || info.Length != beforeLength || info.LastWriteTimeUtc != beforeWrite
            || IsReparsePoint(info))
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        return output.ToArray();
    }
    private static OriginalFile CaptureFile(string path, NativeCoverageExecutionOptions limits)
    {
        var before = new FileInfo(path);
        if (!before.Exists || before.Length < 0 || before.Length > limits.MaximumFileBytes || IsReparsePoint(before))
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[limits.ReadBufferBytes];
        long length = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            length += read;
            if (length > limits.MaximumFileBytes)
            { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
            digest.AppendData(buffer, 0, read);
        }
        var after = new FileInfo(path);
        if (length != before.Length || after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc
            || IsReparsePoint(after))
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        return new(path, length, before.LastWriteTimeUtc, Convert.ToHexStringLower(digest.GetHashAndReset()));
    }
    private static long SumBytes(IReadOnlyDictionary<string, OriginalFile> originals)
    {
        long total = 0;
        foreach (var file in originals.Values)
        { total = checked(total + file.Length); }
        return total;
    }
    private static void WriteCreateOnly(string path, byte[] contents)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(contents, 0, contents.Length);
        stream.Flush(flushToDisk: true);
    }
    private static bool IsReparsePoint(FileSystemInfo info) => info.LinkTarget is not null
        || (info.Attributes & FileAttributes.ReparsePoint) != 0;
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
