using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaNativeFiles : IDisposable
{
    private const string DirectoryPrefix = "keyload-native-replica-";
    private const string GuidFormat = "N";
    private const string VoterA = "voter-a";
    private const string VoterB = "voter-b";
    private const string VoterC = "voter-c";
    private const string JournalFileName = "commands.wal";
    private const int AppendBytes = 4_096;
    private const int SigningKeyBytes = 32;
    private readonly string directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    internal ReplicaConfiguration Configuration
    {
        get;
    }
    internal ReadOnlyMemory<byte> SigningKey
    {
        get;
    }
    internal string JournalPath => Path.Combine(Configuration.Directory, JournalFileName);

    internal ReplicaNativeFiles(Guid? incarnation = null, ReadOnlyMemory<byte>? signingKey = null)
    {
        Configuration = new(VoterA, [VoterA, VoterB, VoterC], Path.Combine(directory, ReplicaProtocol.ReplicaDirectory),
            incarnation ?? Guid.NewGuid())
        {
            MaxAppendBytes = AppendBytes
        };
        SigningKey = signingKey?.ToArray() ?? RandomNumberGenerator.GetBytes(SigningKeyBytes);
    }

    internal ZoneTreeStore Open() => new(new(Configuration.Directory) { Incarnation = Configuration.Incarnation, SigningKey = SigningKey }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
    internal ZoneTreeStore Open(string name) => new(new(Path.Combine(directory, name))
    {
        Incarnation = Configuration.Incarnation,
        SigningKey = SigningKey
    }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
    public void Dispose() => Directory.Delete(directory, true);
}
