using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server;

/// <summary>Dispatches an explicit local offline upgrade before any server or replica starts.</summary>
internal static class ServerOfflineFormatUpgrade
{
    private const string Command = "upgrade-native-store";
    private const string SourceOption = "--source=";
    private const string DestinationOption = "--destination=";
    private const string Usage = "Use upgrade-native-store --source=<stopped-store> --destination=<new-store>.";
    private const string Complete = "Native store upgrade completed. Verify every RF3 store before starting compatible voters.";
    private const int ArgumentCount = 3;
    private const int MaximumPathCharacters = 4096;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || !string.Equals(args[0], Command, StringComparison.Ordinal))
        {
            return false;
        }

        var paths = Parse(args);
        _ = ZoneTreeFormatUpgrade.Upgrade(paths.Source, new(paths.Destination));
        await Console.Out.WriteLineAsync(Complete).ConfigureAwait(false);
        return true;
    }

    private static (string Source, string Destination) Parse(string[] args)
    {
        if (args.Length != ArgumentCount)
        {
            throw Errors.Fail(ErrorCode.Validation, Usage);
        }

        string? source = null;
        string? destination = null;
        foreach (var argument in args.AsSpan(1))
        {
            if (argument.StartsWith(SourceOption, StringComparison.Ordinal) && source is null)
            {
                source = PathArgument(argument, SourceOption);
            }
            else if (argument.StartsWith(DestinationOption, StringComparison.Ordinal) && destination is null)
            {
                destination = PathArgument(argument, DestinationOption);
            }
            else
            {
                throw Errors.Fail(ErrorCode.Validation, Usage);
            }
        }
        return (source ?? throw Errors.Fail(ErrorCode.Validation, Usage),
            destination ?? throw Errors.Fail(ErrorCode.Validation, Usage));
    }

    private static string PathArgument(string argument, string prefix)
    {
        var value = argument[prefix.Length..];
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumPathCharacters || value.Contains('\0', StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Validation, Usage);
        }
        return value;
    }
}
