using Microsoft.Extensions.Options;
using ZoneTree.AbstractFileStream;
namespace KeyLoad.Server.Search;

internal sealed class TextPathAccess(string root, string leaf, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership resources)
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
        resources.MutatePhysical(() => TrackOwnedPath(path, directory));
    }

    private void TrackOwnedPath(string path, bool directory)
    {
        lock (gate)
        {
            TextFiles.RuntimeTrackPath(root, leaf, sourceNodeId, path, directory, executionOptions: executionOptions, resources: resources);
        }
    }

    internal void CreateDirectory(string path, IFileStreamProvider provider)
    {
        var full = Resolve(path);
        resources.MutatePhysical(() => CreateOwnedDirectory(full, provider));
    }

    private void CreateOwnedDirectory(string path, IFileStreamProvider provider)
    {
        TrackDirectoryChain(path);
        provider.CreateDirectory(path);
        SetPrivateDirectoryModes(path);
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

    internal void PrepareReplaceTargets(string first, string? second = null)
    {
        foreach (var path in second is null ? new[] { first } : new[] { first, second })
        {
            if (File.Exists(path))
            {
                Require(path, directory: false);
            }
        }
        resources.MutatePhysical(() =>
        {
            lock (gate)
            {
                TextFiles.RuntimeTrackFiles(root, leaf, sourceNodeId, first, second, executionOptions, resources);
            }
        });
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
