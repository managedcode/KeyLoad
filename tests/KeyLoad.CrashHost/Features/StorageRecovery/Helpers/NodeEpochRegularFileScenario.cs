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
            Console.Out.WriteLine("UnexpectedSuccess");
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
                    FileAccess.Read, FileShare.Read, 4096);
                await Console.Out.WriteLineAsync("UnexpectedSuccess");
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
        var input = await ReadBoundedProfileAsync();
        return JsonSerializer.Deserialize<EpochPriorNodeProfile>(input, EpochPriorSourceProbe.JsonOptions)
            ?? throw new InvalidDataException("The node profile is invalid.");
    }

    private static async Task<string> ReadBoundedProfileAsync()
    {
        var builder = new StringBuilder();
        var chunk = new char[256];
        var ended = false;
        while (true)
        {
            var count = await Console.In.ReadAsync(chunk.AsMemory());
            if (count == 0)
            { break; }
            for (var index = 0; index < count; index++)
            {
                var character = chunk[index];
                if (character is '\r' or '\n')
                { ended = true; continue; }
                if (ended || builder.Length >= MaximumProfileCharacters)
                { throw new InvalidDataException("The node profile is invalid."); }
                builder.Append(character);
            }
        }
        return builder.ToString();
    }
}
