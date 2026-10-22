using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class CoverageProductEvidenceFiles
{
    private const string ModifiedTrxName = "rejected-unit-functional-01.trx";
    private const string ModifiedDescriptorName = "rejected-product-descriptor.json";
    internal const int RejectedOwnedEntryCount = 3;

    internal sealed record OriginalFile(string Path, long Length, DateTime LastWriteTimeUtc, string Sha256);

    internal static IReadOnlyDictionary<string, OriginalFile> CaptureOriginalFiles(string evidenceRoot,
        CoverageExecutionOptions limits, out int entryCount)
    {
        var inventory = CoverageMergeEvidenceInventory.Read(evidenceRoot, limits, out entryCount);
        var originals = new Dictionary<string, OriginalFile>(StringComparer.Ordinal);
        foreach (var file in inventory)
        {
            originals.Add(Path.GetFullPath(file.SourcePath), CaptureFile(file.SourcePath, limits));
        }
        return originals;
    }

    internal static void AssertOriginalFiles(IReadOnlyDictionary<string, OriginalFile> originals,
        CoverageExecutionOptions limits)
    {
        foreach (var original in originals.Values)
        {
            var current = CaptureFile(original.Path, limits);
            if (current.Length != original.Length || current.LastWriteTimeUtc != original.LastWriteTimeUtc
                || !string.Equals(current.Sha256, original.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
            }
        }
    }
    internal static string SelectOwnedDirectory(string evidenceRoot)
    {
        var root = Path.GetFullPath(evidenceRoot);
        var parent = new DirectoryInfo(root);
        if (!parent.Exists || IsReparsePoint(parent))
        {
            throw new IOException(CoverageMergeProcess.OutputFailure);
        }
        var path = Path.Combine(root, "unit-admission-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException(CoverageMergeProcess.OutputFailure);
        }
        return path;
    }
    internal static void CreateOwnedDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || IsReparsePoint(directory))
        {
            throw new IOException(CoverageMergeProcess.OutputFailure);
        }

    }
    internal static void DeleteOwnedDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }
        if (IsReparsePoint(directory))
        {
            throw new IOException(CoverageMergeProcess.OutputFailure);
        }
        var files = Directory.GetFiles(path);
        if (Directory.GetDirectories(path).Length != 0 || files.Any(file =>
                Path.GetFileName(file) is not (ModifiedTrxName or ModifiedDescriptorName)
                || IsReparsePoint(new FileInfo(file))))
        {
            throw new IOException(CoverageMergeProcess.OutputFailure);
        }
        foreach (var file in files)
        {
            File.Delete(file);
        }
        Directory.Delete(path);
    }
    internal static string WriteRejectedCopies(string evidenceRoot, string descriptorPath,
        string temporaryRoot, IReadOnlyDictionary<string, OriginalFile> originals,
        CoverageExecutionOptions limits)
    {
        var descriptorBytes = CoverageEvidenceInputReader.ReadBoundedBytes(descriptorPath, limits.MaximumDescriptorBytes, limits.ReadBufferBytes);
        var descriptor = JsonNode.Parse(descriptorBytes)
            ?? throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        var sourceReference = GetUnitGroupTrxReference(descriptor);
        var sourceTrx = CoverageEvidenceInputReader.ResolveEvidenceReference(evidenceRoot, (string?)sourceReference[CoverageProductFields.Path]);
        var rejectedTrx = ChangeOneOutcomeToFailure(CoverageEvidenceInputReader.ReadBoundedBytes(sourceTrx,
            limits.MaximumReportBytes, limits.ReadBufferBytes));
        var rejectedDescriptor = JsonNode.Parse(descriptorBytes)
            ?? throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        var rejectedReference = GetUnitGroupTrxReference(rejectedDescriptor);
        var trxPath = Path.Combine(temporaryRoot, ModifiedTrxName);
        var relativeTrx = Path.GetRelativePath(evidenceRoot, trxPath).Replace(Path.DirectorySeparatorChar, '/');
        rejectedReference[CoverageProductFields.Path] = relativeTrx;
        rejectedReference[CoverageProductFields.Length] = rejectedTrx.LongLength;
        rejectedReference[CoverageProductFields.Sha256] = Convert.ToHexStringLower(SHA256.HashData(rejectedTrx));
        var rejectedDescriptorBytes = System.Text.Encoding.UTF8.GetBytes(rejectedDescriptor.ToJsonString());
        var originalBytes = SumBytes(originals);
        if (rejectedTrx.LongLength > limits.MaximumReportBytes || rejectedTrx.LongLength > limits.MaximumFileBytes
            || rejectedDescriptorBytes.LongLength > limits.MaximumDescriptorBytes
            || rejectedDescriptorBytes.LongLength > limits.MaximumFileBytes
            || originalBytes > limits.MaximumTotalBytes - rejectedTrx.LongLength - rejectedDescriptorBytes.LongLength)
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        WriteCreateOnly(trxPath, rejectedTrx);
        var rejectedDescriptorPath = WriteDescriptorCopy(temporaryRoot, rejectedDescriptorBytes);
        _ = CoverageMergeEvidenceInventory.Read(evidenceRoot, limits, out _);
        return rejectedDescriptorPath;
    }
    private static JsonNode GetUnitGroupTrxReference(JsonNode descriptor)
    {
        var runs = descriptor[CoverageProductFields.SuiteRuns]?.AsArray()
            ?? throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        var run = runs.SingleOrDefault(item => string.Equals((string?)item?[CoverageProductFields.Suite],
            "unit-functional-01", StringComparison.Ordinal))
            ?? throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        return run[CoverageProductFields.Trx] ?? throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
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
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = new XmlDocument { XmlResolver = null };
        document.Load(reader);
        var results = document.GetElementsByTagName("UnitTestResult");
        if (results.Count == 0 || results[0] is not XmlElement result
            || !string.Equals(result.GetAttribute("outcome"), "Passed", StringComparison.Ordinal))
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        result.SetAttribute("outcome", "Failed");
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
    private static OriginalFile CaptureFile(string path, CoverageExecutionOptions limits)
    {
        var before = new FileInfo(path);
        if (!before.Exists || before.Length < 0 || before.Length > limits.MaximumFileBytes || IsReparsePoint(before))
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[limits.ReadBufferBytes];
        long length = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            length += read;
            if (length > limits.MaximumFileBytes)
            {
                throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
            }
            digest.AppendData(buffer, 0, read);
        }
        var after = new FileInfo(path);
        if (length != before.Length || after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc
            || IsReparsePoint(after))
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        return new(path, length, before.LastWriteTimeUtc, Convert.ToHexStringLower(digest.GetHashAndReset()));
    }
    private static long SumBytes(IReadOnlyDictionary<string, OriginalFile> originals)
    {
        long total = 0;
        foreach (var file in originals.Values)
        {
            total = checked(total + file.Length);
        }
        return total;
    }
    private static void WriteCreateOnly(string path, byte[] contents)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(contents, 0, contents.Length);
        stream.Flush(flushToDisk: true);
    }
    internal static bool IsReparsePoint(FileSystemInfo info) => info.LinkTarget is not null
        || (info.Attributes & FileAttributes.ReparsePoint) != 0;
}
