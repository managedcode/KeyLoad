using System.Text;
using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage.IO;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class NodeEpochRegularFileScenario
{
    internal const string Mode = "node-epoch-regular-file";
    internal const string ReplacementMode = "node-epoch-regular-file-replacement";
    internal const string ImageMode = "node-epoch-regular-image";
    private const int ArgumentCount = 3;
    private const int SourceArgument = 0;
    private const int DestinationArgument = 1;
    private const int ModeArgument = 2;
    private const int MaximumProfileCharacters = 16_384;
    private const string UnexpectedSuccess = "UnexpectedSuccess";

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != ArgumentCount || !IsSupportedMode(args[ModeArgument]))
        { return false; }

        var profile = await ReadProfileAsync();
        if (string.Equals(args[ModeArgument], ReplacementMode, StringComparison.Ordinal))
        {
            await RunReplacementProbeAsync(args[SourceArgument], args[DestinationArgument]);
            return true;
        }
        if (string.Equals(args[ModeArgument], ImageMode, StringComparison.Ordinal))
        {
            RunImageProbe(args[SourceArgument], args[DestinationArgument], profile.Incarnation);
            return true;
        }
        var options = NodeEpochCrashSettings.CreateOptions(profile, args[DestinationArgument]);
        try
        {
            _ = ServerNodeFormatUpgrade.Prepare(args[SourceArgument], CrashServerRuntimeOptions.Runtime(options));
            await Console.Out.WriteLineAsync(UnexpectedSuccess);
        }
        catch (KeyLoadException failure)
        {
            await Console.Out.WriteLineAsync(failure.Code.ToString());
        }
        return true;
    }

    private static bool IsSupportedMode(string mode)
        => string.Equals(mode, Mode, StringComparison.Ordinal)
            || string.Equals(mode, ReplacementMode, StringComparison.Ordinal)
            || string.Equals(mode, ImageMode, StringComparison.Ordinal);

    private static void RunImageProbe(string source, string destination, Guid incarnation)
    {
        try
        {
            _ = ZoneTreeSnapshotFormatUpgrade.Upgrade(source, destination, incarnation, CrashExecutionOptions.StorageExecution());
            Console.Out.WriteLine(nameof(UnexpectedSuccess));
        }
        catch (KeyLoadException failure)
        {
            Console.Out.WriteLine(failure.Code.ToString());
        }
    }

    private static async Task RunReplacementProbeAsync(string source, string retained)
    {
        var identity = OfflineRegularFile.Inspect(source);
        File.Move(source, retained);
        await CreateFifoAsync(source);
        try
        {
            try
            {
                using var unexpected = OfflineRegularFile.OpenWithIdentity(source, identity,
                    FileAccess.Read, FileShare.Read, CrashExecutionOptions.Child().Value.AuthorityReadBufferBytes);
                await Console.Out.WriteLineAsync(nameof(UnexpectedSuccess));
            }
            catch (KeyLoadException failure)
            {
                await Console.Out.WriteLineAsync(failure.Code.ToString());
            }
        }
        finally { File.Delete(source); }
    }

    private static async Task CreateFifoAsync(string path)
    {
        await NodeEpochRegularFileFifoUtility.CreateAsync(path);
    }

    private static async Task<EpochPriorNodeProfile> ReadProfileAsync()
    {
        const string ReadProfileAsyncMessageText = "The node profile is invalid.";

        var input = await ReadBoundedProfileAsync();
        return JsonSerializer.Deserialize<EpochPriorNodeProfile>(input, EpochPriorSourceProbe.JsonOptions)
            ?? throw new InvalidDataException(ReadProfileAsyncMessageText);
    }

    private static async Task<string> ReadBoundedProfileAsync()
    {
        const int EmptyCount = 0;
        const int IndexInitialValue = 0;
        const char CarriageReturnCharacter = '\r';
        const char LineFeedCharacter = '\n';
        const string ReadBoundedProfileAsyncMessageText = "The node profile is invalid.";

        var builder = new StringBuilder();
        var chunk = new char[CrashExecutionOptions.Child().Value.ProfileReadChunkCharacters];
        var ended = false;
        while (true)
        {
            var count = await Console.In.ReadAsync(chunk.AsMemory());
            if (count == EmptyCount)
            { break; }
            for (var index = IndexInitialValue; index < count; index++)
            {
                var character = chunk[index];
                if (character is CarriageReturnCharacter or LineFeedCharacter)
                { ended = true; continue; }
                if (ended || builder.Length >= MaximumProfileCharacters)
                { throw new InvalidDataException(ReadBoundedProfileAsyncMessageText); }
                builder.Append(character);
            }
        }
        return builder.ToString();
    }
}
