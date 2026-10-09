namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>One native exclusive file owner spans all original slots and publication for this destination.</summary>
internal static class ClusterRestoreDestinationOwner
{
    private const string OwnerSuffix = ".keyload-restore-owner";
    private const string OperationPrefix = ".keyload-restore-";
    private const string OperationFormat = "N";
    private const string Invalid = "The native restore destination or operation owner is foreign or unavailable.";

    internal static FileStream Acquire(string destination, Microsoft.Extensions.Options.IOptions<KeyLoad.Storage.ZoneTree.ZoneTreeStorageExecutionOptions> storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var policy = storage.Value;
        policy.Validate();
        var owner = destination + OwnerSuffix;
        ClusterRestorePathValidation.RequireAncestors(destination);
        if (Directory.Exists(owner) || File.Exists(owner)
            && (File.GetAttributes(owner) & FileAttributes.ReparsePoint) != default)
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        return new(owner, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            policy.StreamBufferBytes, FileOptions.WriteThrough);
    }

    internal static string OperationRoot(string destination, Guid operationId)
    {
        if (operationId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var parent = Path.GetDirectoryName(destination) ?? throw Errors.Fail(ErrorCode.Validation, Invalid);
        var root = Path.Combine(parent, OperationPrefix + operationId.ToString(OperationFormat));
        ClusterRestorePathValidation.RequireAncestors(root);
        return root;
    }
}
