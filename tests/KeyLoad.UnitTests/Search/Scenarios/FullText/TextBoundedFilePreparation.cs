using System.Globalization;

namespace KeyLoad.UnitTests.Search;

internal static class TextBoundedFilePreparation
{
    internal const string BoundFilePrefix = "bound-file-";

    internal static async Task<string[]> AddFilesAsync(string directory, int existingCount, int totalCount,
        CancellationToken cancellationToken)
    {
        var added = new string[totalCount - existingCount];
        for (var index = existingCount; index < totalCount; index++)
        {
            var path = Path.Combine(directory, BoundFilePrefix + index.ToString("D3", CultureInfo.InvariantCulture));
            await File.WriteAllBytesAsync(path, [], cancellationToken);
            added[index - existingCount] = path;
        }
        return added;
    }

    internal static void SetSparseLength(string path, long length)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.SetLength(length);
    }
}
