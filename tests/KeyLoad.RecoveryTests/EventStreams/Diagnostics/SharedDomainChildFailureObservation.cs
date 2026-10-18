using System.Text.Json;
using KeyLoad.RecoveryHost.Features.EventStreams;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

/// <summary>Reads only the joined original child's bounded closed metadata on an existing failure.</summary>
internal static class SharedDomainChildFailureObservation
{
    private const string Kind = "SharedDomainOriginalChildFailure";
    private const string WitnessKey = "KeyLoad.SharedDomainCrash.OriginalChildProgress";
    private const int MaximumCharacters = 1024;
    private const int MissingLength = 0;
    private const int EofProbeBytes = 1;

    internal static async Task ReportAsync(string root, CommitStage stage, int mutationIndex,
        Exception primary, CancellationToken token)
    {
        var path = Path.Combine(root, SharedDomainChildProgressProtocol.FileName);
        SharedDomainChildProgressV1? witness = null;
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != default
                || info.Length <= MissingLength || info.Length > SharedDomainChildProgressProtocol.MaximumBytes)
            {
                throw new InvalidDataException(SharedDomainChildProgressProtocol.Invalid);
            }
            var bytes = await ReadBoundedAsync(path, token);
            witness = NativeSerialization.Deserialize<SharedDomainChildProgressV1>(bytes);
            Validate(witness);
        }
        var text = JsonSerializer.Serialize(new
        {
            Kind,
            RequestedBoundary = stage.ToString(),
            RequestedMutationIndex = mutationIndex,
            OriginalChildAndStreamsJoined = true,
            ProgressPresent = witness is not null,
            LastObservedPhase = witness?.Phase.ToString(),
            SeedOrdinal = witness?.SeedOrdinal,
            ElapsedMilliseconds = witness?.ElapsedMilliseconds,
            StorePosition = witness?.StorePosition
        });
        if (text.Length > MaximumCharacters)
        {
            throw new InvalidDataException(SharedDomainChildProgressProtocol.Invalid);
        }
        primary.Data[WitnessKey] = text;
        Console.WriteLine(text);
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            SharedDomainChildProgressProtocol.MaximumBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        if (length <= MissingLength || length > SharedDomainChildProgressProtocol.MaximumBytes)
        {
            throw new InvalidDataException(SharedDomainChildProgressProtocol.Invalid);
        }
        var bytes = new byte[checked((int)length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        var probe = new byte[EofProbeBytes];
        if (await stream.ReadAsync(probe, token).ConfigureAwait(false) != MissingLength)
        {
            throw new InvalidDataException(SharedDomainChildProgressProtocol.Invalid);
        }
        return bytes;
    }

    private static void Validate(SharedDomainChildProgressV1 witness)
    {
        if (witness.Version != SharedDomainChildProgressProtocol.Version || !Enum.IsDefined(witness.Phase)
            || witness.SeedOrdinal < SharedDomainChildProgressProtocol.InitialOrdinal
            || witness.SeedOrdinal > SharedDomainProtocol.QueueScopeIndex
            || !double.IsFinite(witness.ElapsedMilliseconds) || witness.ElapsedMilliseconds < MissingLength
            || witness.StorePosition < SharedDomainChildProgressProtocol.EmptyPosition)
        {
            throw new InvalidDataException(SharedDomainChildProgressProtocol.Invalid);
        }
    }
}
