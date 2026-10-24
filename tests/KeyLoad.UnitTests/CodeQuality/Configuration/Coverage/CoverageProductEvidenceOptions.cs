
namespace KeyLoad.UnitTests.CodeQuality;

internal static class CoverageProductEvidenceOptions
{
    internal const string FileName = "functional-coverage.execution-options.json";

    internal static string Read(string evidenceRoot, CoverageExecutionOptions options)
    {
        var path = Path.Combine(evidenceRoot, FileName);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > options.MaximumManifestBytes
            || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        var beforeLength = info.Length;
        var beforeWrite = info.LastWriteTimeUtc;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var contents = new MemoryStream();
        var buffer = new byte[options.ReadBufferBytes];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (contents.Length > options.MaximumManifestBytes - read)
            {
                throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
            }
            contents.Write(buffer, 0, read);
        }
        info.Refresh();
        if (!info.Exists || contents.Length != beforeLength || info.Length != beforeLength
            || info.LastWriteTimeUtc != beforeWrite || info.LinkTarget is not null
            || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        return System.Text.Encoding.UTF8.GetString(contents.ToArray());
    }
}
