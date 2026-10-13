using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresErrorDeliveryFiles(Guid session, string voter,
    IOptions<RequestProbeExecutionOptions> configured)
{
    private readonly Lock sync = new();
    private readonly RequestProbeExecutionOptions limits = configured.Value;
    private byte[]? ownerBytes;
    private byte[]? armBytes;

    internal PostgresErrorDeliveryArm? ReadArm()
    {
        lock (sync)
        {
            RequireOwner();
            var path = Path.Combine(PostgresErrorDeliveryProtocol.Root, PostgresErrorDeliveryProtocol.Arm);
            if (!File.Exists(path))
            { return null; }
            var exact = Read(path);
            if (armBytes is not null && !exact.AsSpan().SequenceEqual(armBytes))
            { throw Invalid(); }
            armBytes ??= exact.ToArray();
            var arm = NativeSerialization.Deserialize<PostgresErrorDeliveryArm>(exact);
            if (arm.Session != session || arm.Id == Guid.Empty
                || string.IsNullOrEmpty(arm.Subject) || !arm.Subject.StartsWith(SubjectPrefix, StringComparison.Ordinal)
                || System.Text.Encoding.UTF8.GetByteCount(arm.Subject) > limits.MaximumPrincipalBytes)
            { throw Invalid(); }
            return arm;
        }
    }

    internal bool WasClaimed(Guid arm)
    {
        lock (sync)
        {
            RequireOwner();
            return File.Exists(Path.Combine(PostgresErrorDeliveryProtocol.Root,
                PostgresErrorDeliveryProtocol.WitnessPrefix + arm.ToString(PostgresErrorDeliveryProtocol.IdentityFormat)
                + NameSeparator + PostgresErrorDeliveryPhase.Buffered + PostgresErrorDeliveryProtocol.Suffix));
        }
    }

    internal void Write(PostgresErrorDeliveryWitness witness)
    {
        lock (sync)
        {
            RequireOwner();
            var arm = ReadArm() ?? throw Invalid();
            if (witness.Session != session || witness.Arm != arm.Id || witness.Connection == Guid.Empty
                || witness.Primary != ErrorCode.PermissionDenied || !Enum.IsDefined(witness.Phase))
            { throw Invalid(); }
            var bytes = NativeSerialization.Serialize(witness);
            var path = Path.Combine(PostgresErrorDeliveryProtocol.Root, Name(witness));
            if (bytes.Length > limits.MaximumRecordBytes)
            { throw Invalid(); }
            var present = Directory.EnumerateFiles(PostgresErrorDeliveryProtocol.Root).ToArray();
            if (present.Length >= limits.MaximumFiles
                || present.Sum(item => new FileInfo(item).Length) + bytes.Length > limits.MaximumAggregateBytes)
            { throw Invalid(); }
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() =>
            {
                using var output = new FileStream(path + PostgresErrorDeliveryProtocol.PendingSuffix, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                    limits.FileBufferBytes, FileOptions.None);
                ServerFailureObserver.Observe(() => { output.Write(bytes); output.Flush(); }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            File.Move(path + PostgresErrorDeliveryProtocol.PendingSuffix, path, overwrite: false);
            RequireInventory();
        }
    }

    internal static string Name(PostgresErrorDeliveryWitness witness)
        => PostgresErrorDeliveryProtocol.WitnessPrefix
            + witness.Arm.ToString(PostgresErrorDeliveryProtocol.IdentityFormat)
            + NameSeparator + witness.Phase + PostgresErrorDeliveryProtocol.Suffix;

    private void RequireOwner()
    {
        RequireInventory();
        var exact = Read(Path.Combine(PostgresErrorDeliveryProtocol.Root, PostgresErrorDeliveryProtocol.Owner));
        if (ownerBytes is not null && !exact.AsSpan().SequenceEqual(ownerBytes))
        { throw Invalid(); }
        ownerBytes ??= exact.ToArray();
        var owner = NativeSerialization.Deserialize<PostgresErrorDeliveryOwner>(exact);
        if (owner.Session != session || owner.Voter != voter)
        { throw Invalid(); }
    }

    private void RequireInventory()
    {
        var root = new DirectoryInfo(PostgresErrorDeliveryProtocol.Root);
        if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != default(FileAttributes))
        { throw Invalid(); }
        var paths = root.EnumerateFileSystemInfos().Take(checked(limits.MaximumFiles + PostgresErrorDeliveryProtocol.One)).ToArray();
        if (paths.Length > limits.MaximumFiles)
        { throw Invalid(); }
        long total = PostgresErrorDeliveryProtocol.None;
        foreach (var path in paths)
        {
            if (path is not FileInfo file || (file.Attributes & FileAttributes.ReparsePoint) != default(FileAttributes)
                || file.Name != PostgresErrorDeliveryProtocol.Owner && file.Name != PostgresErrorDeliveryProtocol.Arm
                    && !file.Name.StartsWith(PostgresErrorDeliveryProtocol.WitnessPrefix, StringComparison.Ordinal))
            { throw Invalid(); }
            if (file.Name.StartsWith(PostgresErrorDeliveryProtocol.WitnessPrefix, StringComparison.Ordinal)
                && !file.Name.EndsWith(PostgresErrorDeliveryProtocol.PendingSuffix, StringComparison.Ordinal))
            {
                var witness = NativeSerialization.Deserialize<PostgresErrorDeliveryWitness>(Read(file.FullName));
                if (witness.Session != session || witness.Arm == Guid.Empty || witness.Connection == Guid.Empty
                    || !Enum.IsDefined(witness.Phase) || witness.Primary != ErrorCode.PermissionDenied
                    || file.Name != Name(witness))
                { throw Invalid(); }
            }
            total = checked(total + file.Length);
            if (file.Length > limits.MaximumRecordBytes || total > limits.MaximumAggregateBytes)
            { throw Invalid(); }
        }
    }

    private byte[] Read(string path)
    {
        byte[]? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                limits.FileBufferBytes, FileOptions.SequentialScan);
            ServerFailureObserver.Observe(() =>
            {
                if (input.Length > limits.MaximumRecordBytes || input.Length <= PostgresErrorDeliveryProtocol.None)
                { throw Invalid(); }
                result = new byte[checked((int)input.Length)];
                input.ReadExactly(result);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Invalid();
    }

    private const string SubjectPrefix = "c1-probe-";
    private const string NameSeparator = "-";
    private static InvalidOperationException Invalid() => new(PostgresErrorDeliveryProtocol.Invalid);
}
