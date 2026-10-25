namespace KeyLoad.FunctionalTests.CodeQuality;

internal static class CoverageEvidenceInputReader
{
    internal static string ResolveEvidenceReference(string evidenceRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceRoot));
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        return path;
    }

    internal static byte[] ReadBoundedBytes(string path, long maximumBytes, int readBufferBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes || CoverageProductEvidenceFiles.IsReparsePoint(info))
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        var beforeLength = info.Length;
        var beforeWrite = info.LastWriteTimeUtc;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new MemoryStream();
        var buffer = new byte[readBufferBytes];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length > maximumBytes - read)
            {
                throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
            }
            output.Write(buffer, 0, read);
        }
        info.Refresh();
        if (output.Length != beforeLength || info.Length != beforeLength || info.LastWriteTimeUtc != beforeWrite
            || CoverageProductEvidenceFiles.IsReparsePoint(info))
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        return output.ToArray();
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
