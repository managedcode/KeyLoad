using System.Security.Cryptography;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns one original unpublished slot; it never removes failed or foreign retained stages.</summary>
internal static class ZoneTreeRestoreSlotAdmissionOwner
{
    internal const string AdmissionFile = "restore-slot.native";
    private const string OwnerFile = "restore-slot.owner.lock";
    private const ulong Magic = 0x3153524C434C4BUL;
    private const int Empty = 0;
    private const int NativeErrorCodeMask = 0xFFFF;
    private const int SharingViolation = 32;
    private const string Invalid = "The retained native restore slot identity is missing or inconsistent.";

    internal static FileStream Acquire(string stage, ClusterRestoreSlotContext context,
        ZoneTreeBackupRestoreManifestFile[] sourceFiles, ZoneTreeStorageExecutionOptions policy, TimeProvider clock, bool requireExisting, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        stage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stage));
        if (requireExisting && (!Directory.Exists(stage) || !File.Exists(Path.Combine(stage, AdmissionFile))
            || !File.Exists(Path.Combine(stage, OwnerFile))))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        ZoneTreeStoreFiles.CreatePrivateDirectory(stage);
        var ownerPath = Path.Combine(stage, OwnerFile);
        if (File.Exists(ownerPath))
        { RequireRegular(ownerPath); }
        FileStream owner;
        try
        { owner = new(ownerPath, requireExisting ? FileMode.Open : FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, policy.StreamBufferBytes); }
        catch (IOException failure) when ((failure.HResult & NativeErrorCodeMask) == SharingViolation)
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        try
        {
            var admissionPath = Path.Combine(stage, AdmissionFile);
            if (File.Exists(admissionPath))
            {
                var original = Read(admissionPath, policy);
                if (original.Version != ZoneTreeRestoreSlotAdmission.CurrentVersion || original.Context != context)
                { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
                ZoneTreeRestoreSlotSource.RequireSame(original.SourceFiles, sourceFiles);
            }
            else
            {
                if (Directory.EnumerateFileSystemEntries(stage).Any(path => path != ownerPath))
                { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
                Write(admissionPath, new(ZoneTreeRestoreSlotAdmission.CurrentVersion, context, sourceFiles,
                    clock.GetUtcNow()), policy);
            }
            ct.ThrowIfCancellationRequested();
            return owner;
        }
        catch (Exception primary)
        {
            try
            { owner.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    private static ZoneTreeRestoreSlotAdmission Read(string path, ZoneTreeStorageExecutionOptions policy)
    {
        RequireRegular(path);
        var bytes = ZoneTreeMetadataFile.Read(path, policy.MaximumBackupManifestBytes, policy.StreamBufferBytes, Invalid);
        var envelope = ZoneTreeMetadataBinary.Read<ZoneTreeRestoreSlotEnvelope>(bytes.Span, Magic, Invalid);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload.Span), envelope.Checksum.Span))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        return NativeSerialization.Deserialize<ZoneTreeRestoreSlotAdmission>(envelope.Payload.Span);
    }

    private static void Write(string path, ZoneTreeRestoreSlotAdmission admission, ZoneTreeStorageExecutionOptions policy)
    {
        var payload = NativeSerialization.Serialize(admission);
        var bytes = ZoneTreeMetadataBinary.Write(new ZoneTreeRestoreSlotEnvelope(payload, SHA256.HashData(payload)), Magic);
        if (bytes.Length > policy.MaximumBackupManifestBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Invalid); }
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, policy.StreamBufferBytes);
        file.Write(bytes);
        file.Flush(true);
    }

    private static void RequireRegular(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != Empty)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
