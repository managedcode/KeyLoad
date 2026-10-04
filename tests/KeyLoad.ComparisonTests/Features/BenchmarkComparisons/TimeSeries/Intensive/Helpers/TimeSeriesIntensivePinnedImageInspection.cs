using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensivePinnedImageInspection
{
    internal static TimeSeriesIntensivePinnedImageConfig ReadImage(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var image = document.RootElement;
        var config = image.GetProperty(TimeSeriesIntensivePinnedImageFields.Config);
        var id = Text(image, TimeSeriesIntensivePinnedImageFields.Id);
        var digests = Strings(image.GetProperty(TimeSeriesIntensivePinnedImageFields.RepoDigests));
        var os = Text(image, TimeSeriesIntensivePinnedImageFields.Os);
        var architecture = Text(image, TimeSeriesIntensivePinnedImageFields.Architecture);
        var entrypoint = Strings(config.GetProperty(TimeSeriesIntensivePinnedImageFields.Entrypoint));
        var command = Strings(config.GetProperty(TimeSeriesIntensivePinnedImageFields.Command));
        var environment = Strings(config.GetProperty(TimeSeriesIntensivePinnedImageFields.Environment));
        Require(TimeSeriesIntensivePinnedImageContext.Matches(id, TimeSeriesIntensivePinnedImageProtocol.ConfigPattern)
            && digests.Any(digest => TimeSeriesIntensivePinnedImageProtocol.AcceptedRepoDigests.Contains(digest, StringComparer.Ordinal))
            && os == TimeSeriesIntensivePinnedImageProtocol.Linux && !string.IsNullOrWhiteSpace(architecture)
            && entrypoint.Length > 0 && command.Length > 0);
        foreach (var item in environment)
        {
            var separator = item.IndexOf('=', StringComparison.Ordinal);
            Require(separator > 0 && !Regex.IsMatch(item[..separator],
                TimeSeriesIntensivePinnedImageFields.EnvironmentSensitivePattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(TimeSeriesIntensivePinnedImageProtocol.RegexSeconds)));
        }

        return new(TimeSeriesIntensivePinnedImageProtocol.Image, id, digests, os, architecture,
            entrypoint, command, Text(config, TimeSeriesIntensivePinnedImageFields.User), environment);
    }

    internal static string ReadContainer(byte[] bytes, string name, string nonce,
        TimeSeriesIntensivePinnedImageConfig? image = null, bool requireExited = false)
    {
        using var document = JsonDocument.Parse(bytes);
        var container = document.RootElement;
        var config = container.GetProperty(TimeSeriesIntensivePinnedImageFields.Config);
        Require(Text(container, TimeSeriesIntensivePinnedImageFields.Name) == Path.AltDirectorySeparatorChar + name
            && Text(config.GetProperty(TimeSeriesIntensivePinnedImageFields.Labels),
                TimeSeriesIntensivePinnedImageProtocol.OwnershipLabel) == nonce);
        if (image is not null)
        {
            Require(Text(container, TimeSeriesIntensivePinnedImageFields.Image) == image.ConfigId
                && TimeSeriesIntensivePinnedImageProtocol.AcceptedImageNames.Contains(
                    Text(config, TimeSeriesIntensivePinnedImageFields.Image), StringComparer.Ordinal)
                && Text(config, TimeSeriesIntensivePinnedImageFields.User) == TimeSeriesIntensivePinnedImageProtocol.RootUser);
        }

        if (requireExited)
        {
            var state = container.GetProperty(TimeSeriesIntensivePinnedImageFields.State);
            Require(!state.GetProperty(TimeSeriesIntensivePinnedImageFields.Running).GetBoolean()
                && state.GetProperty(TimeSeriesIntensivePinnedImageFields.ExitCode).GetInt32() == 0
                && Text(state, TimeSeriesIntensivePinnedImageFields.Status) == TimeSeriesIntensivePinnedImageFields.Exited);
        }

        var id = Text(container, TimeSeriesIntensivePinnedImageFields.Id);
        Require(id.Length == 64 && id.All(char.IsAsciiHexDigitLower));
        return id;
    }

    internal static IReadOnlyDictionary<string, string> ReadObservations(byte[] bytes)
    {
        var output = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            Require(parts.Length == 2 && facts.TryAdd(parts[0], parts[1]));
        }

        Require(facts[TimeSeriesIntensivePinnedImageProtocol.PgMajor] == TimeSeriesIntensivePinnedImageProtocol.PostgresMajor
            && facts[TimeSeriesIntensivePinnedImageProtocol.RootUid] == TimeSeriesIntensivePinnedImageProtocol.RootUser
            && facts[TimeSeriesIntensivePinnedImageProtocol.PgData] == TimeSeriesIntensivePinnedImageProtocol.ProposedPgData
            && facts[TimeSeriesIntensivePinnedImageProtocol.WriteVerified] == TimeSeriesIntensivePinnedImageProtocol.True
            && TimeSeriesIntensivePinnedImageContext.Matches(facts[TimeSeriesIntensivePinnedImageProtocol.PostgresUid],
                TimeSeriesIntensivePinnedImageProtocol.DecimalPattern)
            && facts[TimeSeriesIntensivePinnedImageProtocol.SwitchName] is TimeSeriesIntensivePinnedImageProtocol.Gosu
                or TimeSeriesIntensivePinnedImageProtocol.SuExec);
        foreach (var tool in TimeSeriesIntensivePinnedImageProtocol.RequiredTools)
        {
            RequirePath(facts[TimeSeriesIntensivePinnedImageProtocol.ToolPrefix + tool]);
        }

        RequirePath(facts[TimeSeriesIntensivePinnedImageProtocol.EntryPoint]);
        RequirePath(facts[TimeSeriesIntensivePinnedImageProtocol.SwitchPath]);
        return facts;
    }

    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidDataException(TimeSeriesIntensivePinnedImageProtocol.ImageInvalid);
        }
    }

    private static void RequirePath(string value) => Require(TimeSeriesIntensivePinnedImageContext.Matches(value,
        TimeSeriesIntensivePinnedImageProtocol.PathPattern));

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()
        ?? throw new InvalidDataException(TimeSeriesIntensivePinnedImageProtocol.ImageInvalid);

    private static string[] Strings(JsonElement element) => element.EnumerateArray()
        .Select(item => item.GetString() ?? throw new InvalidDataException(TimeSeriesIntensivePinnedImageProtocol.ImageInvalid)).ToArray();
}
