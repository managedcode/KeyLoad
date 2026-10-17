using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class PostgresErrorReceiverControl
{
    private readonly Dictionary<string, string> directories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> arms = new(StringComparer.Ordinal);
    private readonly Lock sync = new();
    private bool retained;
    private bool joined;
    private bool terminal;
    private readonly Dictionary<string, byte[]> witnesses = new(StringComparer.Ordinal);
    internal Guid Session
    {
        get;
    }
    internal string Root
    {
        get;
    }

    internal PostgresErrorReceiverControl(string dataRoot, Guid session)
    {
        Session = session;
        Root = Path.Combine(dataRoot, PostgresErrorReceiverProtocol.Directory);
        if (Directory.Exists(Root))
        {
            throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
        }
        Directory.CreateDirectory(Root);
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var path = Path.Combine(Root, node);
            Directory.CreateDirectory(path);
            var bytes = NativeSerialization.Serialize(new PostgresErrorDeliveryOwner(session,
                RequestCqrsProbeFileNames.OriginForNode(node)));
            PostgresErrorReceiverFiles.Write(path, PostgresErrorDeliveryProtocol.Owner, bytes);
            directories.Add(node, path);
            owners.Add(node, bytes);
        }
    }

    internal string NodeDirectory(string node) => directories[node];
    internal void RetainEvidence()
    {
        lock (sync)
        {
            retained = true;
        }
    }

    internal Guid Arm(string node, string subject)
    {
        lock (sync)
        {
            RequireOwners();
            if (joined || arms.Count != PostgresRf3Protocol.FirstColumn)
            {
                throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
            }
            var id = Guid.NewGuid();
            var bytes = NativeSerialization.Serialize(new PostgresErrorDeliveryArm(Session, id, subject));
            PostgresErrorReceiverFiles.Write(directories[node], PostgresErrorDeliveryProtocol.Arm, bytes);
            arms.Add(node, bytes);
            return id;
        }
    }

    internal async Task<PostgresErrorDeliveryWitness> WaitAsync(string node, Guid arm,
        PostgresErrorDeliveryPhase phase, CancellationToken token)
    {
        if (!token.CanBeCanceled)
        {
            throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
        }
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (sync)
            {
                RequireOwners();
                var name = PostgresErrorDeliveryProtocol.WitnessPrefix
                    + arm.ToString(PostgresErrorDeliveryProtocol.IdentityFormat) + "-" + phase + PostgresErrorDeliveryProtocol.Suffix;
                var path = Path.Combine(directories[node], name);
                if (File.Exists(path))
                {
                    return ReadWitness(path, name, arm, phase);
                }
            }
            await Task.Delay(PostgresErrorReceiverProtocol.Poll, TimeProvider.System, token);
        }
    }

    private PostgresErrorDeliveryWitness ReadWitness(string path, string name, Guid arm,
        PostgresErrorDeliveryPhase phase)
    {
        var witness = NativeSerialization.Deserialize<PostgresErrorDeliveryWitness>(PostgresErrorReceiverFiles.Read(path));
        if (witness.Session != Session || witness.Arm != arm || witness.Phase != phase
            || witness.Connection == Guid.Empty || witness.Primary != ErrorCode.PermissionDenied)
        {
            throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
        }
        var exact = PostgresErrorReceiverFiles.Read(path);
        if (witnesses.TryGetValue(name, out var before) && !before.AsSpan().SequenceEqual(exact))
        {
            throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
        }
        witnesses[name] = exact;
        terminal |= phase == PostgresErrorDeliveryPhase.ConnectionJoined;
        return witness;
    }

    private void RequireOwners()
    {
        foreach (var node in directories.Keys)
        {
            PostgresErrorReceiverFiles.RequireInventory(directories[node]);
            if (!PostgresErrorReceiverFiles.Read(Path.Combine(directories[node],
                PostgresErrorDeliveryProtocol.Owner)).AsSpan().SequenceEqual(owners[node]))
            {
                throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
            }
            if (arms.TryGetValue(node, out var expected) && !PostgresErrorReceiverFiles.Read(
                Path.Combine(directories[node], PostgresErrorDeliveryProtocol.Arm)).AsSpan().SequenceEqual(expected))
            {
                throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
            }
        }
        foreach (var entry in witnesses)
        {
            if (!PostgresErrorReceiverFiles.Read(Path.Combine(directories[McpCallerProtocol.Node3], entry.Key))
                .AsSpan().SequenceEqual(entry.Value))
            {
                throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
            }
        }
    }

    internal Task JoinAfterResourcesAsync(bool resourcesJoined)
    {
        lock (sync)
        {
            if (!resourcesJoined)
            {
                retained = true;
                throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
            }
            RequireOwners();
            joined = true;
            if (!retained && arms.Count != PostgresRf3Protocol.FirstColumn && !terminal)
            {
                throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
            }
            if (!retained)
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        return Task.CompletedTask;
    }
}
