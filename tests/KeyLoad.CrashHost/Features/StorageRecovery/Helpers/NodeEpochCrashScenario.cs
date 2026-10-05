using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.CrashHost;

internal static class NodeEpochCrashScenario
{
    internal const string Mode = "node-epoch-upgrade";
    private const int ArgumentCount = 4;
    private const int SourceArgument = 0;
    private const int DestinationArgument = 1;
    private const int StageArgument = 2;
    private const int ModeArgument = 3;
    private const int MaximumInputCharacters = 16_384;
    private const int InputChunkCharacters = 256;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != ArgumentCount || !string.Equals(args[ModeArgument], Mode, StringComparison.Ordinal))
        { return false; }

        var stage = ParseStage(args[StageArgument]);
        var profile = await ReadProfileAsync();
        var options = NodeEpochCrashSettings.CreateOptions(profile, args[DestinationArgument]);
        var boundary = new NodeEpochCrashBoundary(stage);
        if (stage == NodeFormatUpgradeStage.Published)
        {
            _ = ServerNodeFormatUpgrade.Prepare(args[SourceArgument], options);
            _ = ServerNodeFormatUpgrade.Publish(args[SourceArgument], options, boundary.Observe);
        }
        else
        {
            _ = ServerNodeFormatUpgrade.Prepare(args[SourceArgument], options, boundary.Observe);
        }
        if (!boundary.Observed)
        { throw new InvalidOperationException("The requested node-upgrade boundary was not reached."); }
        return true;
    }

    private static NodeFormatUpgradeStage ParseStage(string value)
    {
        if (!Enum.TryParse<NodeFormatUpgradeStage>(value, ignoreCase: false, out var stage)
            || !Enum.IsDefined(stage))
        { throw new ArgumentOutOfRangeException(nameof(value), value, "The node-upgrade stage is unsupported."); }
        return stage;
    }

    private static async Task<EpochPriorNodeProfile> ReadProfileAsync()
    {
        var input = new char[MaximumInputCharacters];
        var chunk = new char[InputChunkCharacters];
        var length = 0;
        while (true)
        {
            var count = await Console.In.ReadAsync(chunk.AsMemory());
            if (count == 0)
            {
                return JsonSerializer.Deserialize<EpochPriorNodeProfile>(input.AsSpan(0, length),
                    EpochPriorSourceProbe.JsonOptions)
                    ?? throw new InvalidDataException("The node-upgrade profile is invalid.");
            }
            if (count > MaximumInputCharacters - length)
            { throw new InvalidDataException("The node-upgrade profile exceeds its input bound."); }
            chunk.AsSpan(0, count).CopyTo(input.AsSpan(length));
            length += count;
        }
    }
}

internal sealed class NodeEpochCrashBoundary(NodeFormatUpgradeStage requestedStage)
{
    private int observed;
    internal bool Observed => Volatile.Read(ref observed) != 0;

    internal void Observe(NodeFormatUpgradeStage stage)
    {
        if (stage != requestedStage)
        { return; }
        if (Interlocked.Exchange(ref observed, 1) != 0)
        { throw new InvalidOperationException("The requested node-upgrade boundary was observed twice."); }
        CrashHostPause.AtBoundary();
    }
}

internal static class NodeEpochCrashSettings
{
    private static readonly Guid PhysicalShardId = Guid.Parse("a7c8e9b0-5162-4374-8a91-6d20e7f43519");

    internal static NodeOptions CreateOptions(EpochPriorNodeProfile profile, string destination)
        => new()
        {
            DataDirectory = destination,
            PublicEndpoint = profile.LocalId,
            Peers = profile.Voters,
            PhysicalShardId = PhysicalShardId,
            Incarnation = profile.Incarnation,
            SigningKey = profile.SigningKey,
            PeerSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            AdminKey = profile.AdminKey,
            AllowLoopbackHttp = true
        };
}
