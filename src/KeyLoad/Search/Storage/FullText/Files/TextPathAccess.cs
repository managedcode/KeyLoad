using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Features.Search;

internal sealed class TextPathAccess(string root, string leaf, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
{
    private readonly Lock gate = new();

    internal string Resolve(string path)
    {
        const int EmptyFileGetAttributesFullFileAttributesReparsePoint = 0;

        var full = Path.GetFullPath(path);
        var runtimeRootCore = Path.GetFullPath(Path.Combine(root, leaf, TextProtocol.RuntimeDirectoryCore));
        if (!RuntimeTextPath.IsWithin(runtimeRootCore, full))
        {
            throw TextErrors.Ownership();
        }
        RuntimeTextPath.VerifyContainedAncestors(runtimeRootCore, full);
        try
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != EmptyFileGetAttributesFullFileAttributesReparsePoint)
            {
                throw TextErrors.Ownership();
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        return full;
    }

    internal void Require(string path, bool directory)
    {
        TextFiles.RuntimeRequirePath(root, leaf, sourceNodeId, path, directory, executionOptions: executionOptions);
        if (directory)
        {
            TextFileIO.VerifyDirectory(path);
        }
        else
        {
            TextFileIO.VerifyRegularFile(path);
        }
    }

    internal void Track(string path, bool directory)
    {
        lock (gate)
        {
            TextFiles.RuntimeTrackPath(root, leaf, sourceNodeId, path, directory, executionOptions: executionOptions, resources: resources);
        }
    }

    internal void TrackDirectoryChain(string path)
    {
        var runtimeRootCore = Path.GetFullPath(Path.Combine(root, leaf, TextProtocol.RuntimeDirectoryCore));
        var chain = new Stack<string>();
        for (var current = path; RuntimeTextPath.IsWithin(runtimeRootCore, current) && current != runtimeRootCore;
            current = Path.GetDirectoryName(current)!)
        {
            chain.Push(current);
        }
        while (chain.TryPop(out var directory))
        {
            if (Directory.Exists(directory))
            {
                Require(directory, directory: true);
            }
            else
            {
                Track(directory, directory: true);
            }
        }
        if (Directory.Exists(runtimeRootCore))
        {
            Require(runtimeRootCore, directory: true);
        }
        else
        {
            Track(runtimeRootCore, directory: true);
        }
    }

    internal void PrepareReplaceTarget(string path)
    {
        if (File.Exists(path))
        {
            Require(path, directory: false);
        }
        else
        {
            Track(path, directory: false);
        }
    }

    internal void SetPrivateDirectoryModes(string path)
    {
        var rootPath = Path.GetFullPath(Path.Combine(root, leaf, TextProtocol.RuntimeDirectoryCore));
        for (var current = path; RuntimeTextPath.IsWithin(rootPath, current); current = Path.GetDirectoryName(current)!)
        {
            if (Directory.Exists(current))
            {
                TextFileIO.SetPrivateDirectoryMode(current);
            }
            if (string.Equals(current, rootPath, StringComparison.Ordinal))
            {
                return;
            }
        }
    }
}
