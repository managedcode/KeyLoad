using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextPathAccess(string root, string leaf, Guid sourceNodeId, IOptions<NativeTextExecutionOptions> executionOptions, NativeTextResourceOwnership? resources = null)
{
    private readonly Lock gate = new();

    internal string Resolve(string path)
    {
        const int EmptyFileGetAttributesFullFileAttributesReparsePoint = 0;

        var full = Path.GetFullPath(path);
        var nativeRoot = Path.GetFullPath(Path.Combine(root, leaf, NativeTextProtocol.NativeDirectory));
        if (!NativeTextPath.IsWithin(nativeRoot, full))
        {
            throw NativeTextErrors.Ownership();
        }
        NativeTextPath.VerifyContainedAncestors(nativeRoot, full);
        try
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != EmptyFileGetAttributesFullFileAttributesReparsePoint)
            {
                throw NativeTextErrors.Ownership();
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
        NativeTextFiles.RequireNativePath(root, leaf, sourceNodeId, path, directory, executionOptions: executionOptions);
        if (directory)
        {
            NativeTextFileIO.VerifyDirectory(path);
        }
        else
        {
            NativeTextFileIO.VerifyRegularFile(path);
        }
    }

    internal void Track(string path, bool directory)
    {
        lock (gate)
        {
            NativeTextFiles.TrackNativePath(root, leaf, sourceNodeId, path, directory, executionOptions: executionOptions, resources: resources);
        }
    }

    internal void TrackDirectoryChain(string path)
    {
        var nativeRoot = Path.GetFullPath(Path.Combine(root, leaf, NativeTextProtocol.NativeDirectory));
        var chain = new Stack<string>();
        for (var current = path; NativeTextPath.IsWithin(nativeRoot, current) && current != nativeRoot;
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
        if (Directory.Exists(nativeRoot))
        {
            Require(nativeRoot, directory: true);
        }
        else
        {
            Track(nativeRoot, directory: true);
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
        var rootPath = Path.GetFullPath(Path.Combine(root, leaf, NativeTextProtocol.NativeDirectory));
        for (var current = path; NativeTextPath.IsWithin(rootPath, current); current = Path.GetDirectoryName(current)!)
        {
            if (Directory.Exists(current))
            {
                NativeTextFileIO.SetPrivateDirectoryMode(current);
            }
            if (string.Equals(current, rootPath, StringComparison.Ordinal))
            {
                return;
            }
        }
    }
}
