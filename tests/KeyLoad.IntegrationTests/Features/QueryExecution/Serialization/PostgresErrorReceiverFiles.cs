using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresErrorReceiverFiles
{
    internal static void Write(string root, string name, byte[] bytes)
    {
        RequireInventory(root);
        if (bytes.Length > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes)
        {
            throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
        }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            using var output = new FileStream(Path.Combine(root, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            ServerFailureObserver.Observe(() => { output.Write(bytes); output.Flush(); }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        RequireInventory(root);
    }

    internal static byte[] Read(string path)
    {
        byte[]? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ServerFailureObserver.Observe(() =>
            {
                if (input.Length <= PostgresRf3Protocol.FirstColumn || input.Length > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes)
                {
                    throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
                }
                result = new byte[checked((int)input.Length)];
                input.ReadExactly(result);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
    }

    internal static void RequireInventory(string root)
    {
        var info = new DirectoryInfo(root);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != default(FileAttributes))
        {
            throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
        }
        var files = info.EnumerateFileSystemInfos().Take(RequestCqrsProbeFixtureProtocol.MaximumFilesPerVoter + 1).ToArray();
        if (files.Length > RequestCqrsProbeFixtureProtocol.MaximumFilesPerVoter)
        {
            throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
        }
        long total = PostgresRf3Protocol.FirstColumn;
        foreach (var item in files)
        {
            if (item is not FileInfo file || (file.Attributes & FileAttributes.ReparsePoint) != default(FileAttributes)
                || file.Length > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes
                || file.Name != PostgresErrorDeliveryProtocol.Owner && file.Name != PostgresErrorDeliveryProtocol.Arm
                    && !file.Name.StartsWith(PostgresErrorDeliveryProtocol.WitnessPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
            }
            total = checked(total + file.Length);
            if (total > RequestCqrsProbeFixtureProtocol.MaximumVoterBytes)
            {
                throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
            }
        }
    }
}
