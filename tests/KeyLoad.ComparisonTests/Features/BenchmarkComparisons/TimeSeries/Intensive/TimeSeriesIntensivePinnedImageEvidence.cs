using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensivePinnedImageEvidence(string root)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly List<TimeSeriesIntensivePinnedImageCommandReceipt> commands = [];

    internal IReadOnlyList<TimeSeriesIntensivePinnedImageCommandReceipt> Commands => commands.AsReadOnly();

    internal static async Task<TimeSeriesIntensivePinnedImageEvidence> CreateAsync(CancellationToken token)
    {
        var path = TimeSeriesIntensivePinnedImageContext.Required(TimeSeriesIntensivePinnedImageProtocol.EvidenceEnvironment);
        RequireNewPath(path);
        var result = await TimeSeriesIntensivePinnedImageProcess.RunAsync(TimeSeriesIntensivePinnedImageProtocol.MakeDirectory,
            [TimeSeriesIntensivePinnedImageFields.ModeFlag, TimeSeriesIntensivePinnedImageFields.DirectoryMode,
                TimeSeriesIntensivePinnedImageFields.OptionsEnd, path],
            TimeSeriesIntensivePinnedImageProtocol.OperationSeconds, token);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(TimeSeriesIntensivePinnedImageProtocol.EvidenceInvalid);
        }

        var evidence = new TimeSeriesIntensivePinnedImageEvidence(path);
        await evidence.RetainCommandAsync(TimeSeriesIntensivePinnedImageFields.CreateDirectory, result);
        return evidence;
    }

    internal async Task RetainCommandAsync(string label, TimeSeriesIntensivePinnedImageCommand command)
    {
        if (!TimeSeriesIntensivePinnedImageContext.Matches(label, TimeSeriesIntensivePinnedImageFields.CommandNamePattern))
        {
            throw new InvalidDataException(TimeSeriesIntensivePinnedImageProtocol.EvidenceInvalid);
        }

        var prefix = commands.Count.ToString(TimeSeriesIntensivePinnedImageFields.CommandNumberFormat,
            CultureInfo.InvariantCulture) + TimeSeriesIntensivePinnedImageProtocol.CommandSeparator + label;
        var output = await WriteOutputAsync(prefix + TimeSeriesIntensivePinnedImageFields.StandardOutputSuffix, command.Output);
        var error = await WriteOutputAsync(prefix + TimeSeriesIntensivePinnedImageFields.StandardErrorSuffix, command.Error);
        var receipt = new TimeSeriesIntensivePinnedImageCommandReceipt(command.Executable, command.Arguments,
            command.DeadlineSeconds, command.ExitCode, command.StartedAt, command.CompletedAt, output, error, command.Failures);
        await WriteJsonAsync(prefix + TimeSeriesIntensivePinnedImageFields.JsonSuffix, receipt);
        commands.Add(receipt);
    }

    internal Task WriteJsonAsync<T>(string name, T value) => WriteAsync(name, JsonSerializer.SerializeToUtf8Bytes(value, Options));

    private async Task<TimeSeriesIntensivePinnedImageOutputReceipt> WriteOutputAsync(string name, byte[] bytes)
    {
        await WriteAsync(name, bytes);
        return new(name, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private async Task WriteAsync(string name, byte[] bytes)
    {
        if (Path.GetFileName(name) != name || bytes.Length > TimeSeriesIntensivePinnedImageProtocol.MaximumBytes)
        {
            throw new InvalidDataException(TimeSeriesIntensivePinnedImageProtocol.EvidenceInvalid);
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(TimeSeriesIntensivePinnedImageProtocol.OperationSeconds));
        await using var stream = new FileStream(Path.Combine(root, name), FileMode.CreateNew, FileAccess.Write,
            FileShare.None, TimeSeriesIntensivePinnedImageProtocol.BufferBytes, FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, deadline.Token);
        await stream.FlushAsync(deadline.Token);
    }

    private static void RequireNewPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path || Path.GetDirectoryName(path) is null
            || path.Split(Path.DirectorySeparatorChar).Any(part => part is TimeSeriesIntensivePinnedImageProtocol.CurrentPathSegment
                or TimeSeriesIntensivePinnedImageProtocol.ParentPathSegment)
            || Path.Exists(path) || new FileInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException(TimeSeriesIntensivePinnedImageProtocol.EvidenceInvalid);
        }

        for (var parent = new DirectoryInfo(Path.GetDirectoryName(path)!); parent is not null; parent = parent.Parent)
        {
            if (!parent.Exists || parent.LinkTarget is not null)
            {
                throw new InvalidDataException(TimeSeriesIntensivePinnedImageProtocol.EvidenceInvalid);
            }
        }
    }
}
