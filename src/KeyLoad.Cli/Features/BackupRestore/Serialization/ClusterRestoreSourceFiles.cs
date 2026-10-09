using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Retains independently streamed exact four-file hashes after native archive verification.</summary>
internal static class ClusterRestoreSourceFiles
{
    internal const string EnvelopeName = "catalog-backup.native";
    private const string IdentityName = "identity.json";
    private const string JournalName = "commands.wal";
    private const string ManifestName = "backup.json";
    private const int NoBytes = 0;
    private const int BufferStart = 0;
    private const string Invalid = "The original native source file is linked, absent or exceeds its owning bound.";

    internal static ImmutableArray<ClusterRestoreSourceFile> Read(string directory,
        ZoneTreeStorageExecutionOptions policy, CancellationToken ct)
    {
        string[] names = [IdentityName, JournalName, ManifestName, EnvelopeName];
        return names.Select(name => Hash(directory, name, policy, ct)).ToImmutableArray();
    }

    private static ClusterRestoreSourceFile Hash(string directory, string name,
        ZoneTreeStorageExecutionOptions policy, CancellationToken ct)
    {
        var path = Path.Combine(directory, name);
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != NoBytes)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, policy.StreamBufferBytes);
        var bound = name == IdentityName ? policy.MaximumIdentityFileBytes : policy.MaximumBackupManifestBytes;
        if (input.Length <= NoBytes || name != JournalName && input.Length > bound)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Invalid); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[policy.FileBufferBytes];
        int read;
        while ((read = input.Read(buffer)) > NoBytes)
        { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, BufferStart, read); }
        ct.ThrowIfCancellationRequested();
        return new(name, input.Length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
