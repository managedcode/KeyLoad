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

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        const string TryRunAsyncMessageText = "The requested node-upgrade boundary was not reached.";

        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != ArgumentCount || !string.Equals(args[ModeArgument], Mode, StringComparison.Ordinal))
        { return false; }

        var stage = ParseStage(args[StageArgument]);
        var profile = await ReadProfileAsync();
        var options = NodeEpochCrashSettings.CreateOptions(profile, args[DestinationArgument]);
        var boundary = new NodeEpochCrashBoundary(stage);
        if (stage == NodeFormatUpgradeStage.Published)
        {
            _ = ServerNodeFormatUpgrade.Prepare(args[SourceArgument], CrashServerRuntimeOptions.Runtime(options));
            _ = ServerNodeFormatUpgrade.Publish(args[SourceArgument], CrashServerRuntimeOptions.Runtime(options), boundary.Observe);
        }
        else
        {
            _ = ServerNodeFormatUpgrade.Prepare(args[SourceArgument], CrashServerRuntimeOptions.Runtime(options), boundary.Observe);
        }
        if (!boundary.Observed)
        { throw new InvalidOperationException(TryRunAsyncMessageText); }
        return true;
    }

    private static NodeFormatUpgradeStage ParseStage(string value)
    {
        const string ParseStageMessageText = "The node-upgrade stage is unsupported.";

        if (!Enum.TryParse<NodeFormatUpgradeStage>(value, ignoreCase: false, out var stage)
            || !Enum.IsDefined(stage))
        { throw new ArgumentOutOfRangeException(nameof(value), value, ParseStageMessageText); }
        return stage;
    }

    private static async Task<EpochPriorNodeProfile> ReadProfileAsync()
    {
        const int LengthInitialValue = 0;
        const int EmptyCount = 0;
        const int StartEmptyCount = 0;
        const string ReadProfileAsyncMessageText = "The node-upgrade profile is invalid.";
        const string ReadProfileAsyncReadProfileAsyncMessageText = "The node-upgrade profile exceeds its input bound.";

        var input = new char[MaximumInputCharacters];
        var chunk = new char[CrashExecutionOptions.Child().Value.ProfileReadChunkCharacters];
        var length = LengthInitialValue;
        while (true)
        {
            var count = await Console.In.ReadAsync(chunk.AsMemory());
            if (count == EmptyCount)
            {
                return JsonSerializer.Deserialize<EpochPriorNodeProfile>(input.AsSpan(StartEmptyCount, length),
                    EpochPriorSourceProbe.JsonOptions)
                    ?? throw new InvalidDataException(ReadProfileAsyncMessageText);
            }
            if (count > MaximumInputCharacters - length)
            { throw new InvalidDataException(ReadProfileAsyncReadProfileAsyncMessageText); }
            chunk.AsSpan(StartEmptyCount, count).CopyTo(input.AsSpan(length));
            length += count;
        }
    }
}

internal sealed class NodeEpochCrashBoundary(NodeFormatUpgradeStage requestedStage)
{
    private const int GetObservedEmptyRead = 0;

    private int observed;
    internal bool Observed => Volatile.Read(ref observed) != GetObservedEmptyRead;

    internal void Observe(NodeFormatUpgradeStage stage)
    {
        const int ValueSingleItemCount = 1;
        const int EmptyExchange = 0;
        const string ObserveMessageText = "The requested node-upgrade boundary was observed twice.";

        if (stage != requestedStage)
        { return; }
        if (Interlocked.Exchange(ref observed, ValueSingleItemCount) != EmptyExchange)
        { throw new InvalidOperationException(ObserveMessageText); }
        CrashHostPause.AtBoundary();
    }
}

[ConfigurationBinding]
internal static class NodeEpochCrashSettings
{
    private const int PeerSecretBytes = 32;

    private const string PhysicalShardIdPhysicalShardIdInputText = "a7c8e9b0-5162-4374-8a91-6d20e7f43519";

    private static readonly Guid PhysicalShardId = Guid.Parse(PhysicalShardIdPhysicalShardIdInputText);

    internal static NodeOptions CreateOptions(EpochPriorNodeProfile profile, string destination)
        => new()
        {
            DataDirectory = destination,
            PublicEndpoint = profile.LocalId,
            Peers = profile.Voters,
            PhysicalShardId = PhysicalShardId,
            Incarnation = profile.Incarnation,
            SigningKey = profile.SigningKey,
            PeerSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerSecretBytes)),
            AdminKey = profile.AdminKey,
            AllowLoopbackHttp = true
        };
}
