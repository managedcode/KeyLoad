namespace KeyLoad.UnitTests.ClientApi;

/// <summary>Owns one real temporary wire file used by MCP frame contract tests.</summary>
internal sealed class TemporaryFrameFile : IDisposable
{
    private const string FilePrefix = "keyload-mcp-frame-";
    private const string FileExtension = ".json";

    /// <summary>Creates an isolated temporary file containing the supplied real wire bytes.</summary>
    /// <param name="content">The exact bytes to write before opening a FileStream.</param>
    public TemporaryFrameFile(byte[] content)
    {
        Path = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            $"{FilePrefix}{Guid.NewGuid():N}{FileExtension}");
        File.WriteAllBytes(Path, content);
    }

    /// <summary>Gets the unique temporary file path.</summary>
    public string Path
    {
        get;
    }

    /// <summary>Opens the temporary bytes through a real asynchronous FileStream.</summary>
    /// <returns>The open file stream positioned at its beginning.</returns>
    public FileStream OpenRead()
        => new(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

    /// <summary>Deletes the temporary wire file after its streams have closed.</summary>
    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
