using System.Security.Cryptography;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class MovementFrameObservationNodeFiles
{
    private readonly string directory;
    private readonly byte[] ownerBytes;
    private readonly IOptions<RequestProbeExecutionOptions> options;
    private readonly MovementFrameObservationJson json;
    private readonly bool published;
    private bool deleted;
    private byte[]? selected;
    private byte[]? observation;
    internal MovementFrameObservationNodeFiles(string directory, byte[] ownerBytes,
        IOptions<RequestProbeExecutionOptions> options)
    {
        this.directory = directory;
        this.ownerBytes = ownerBytes;
        this.options = options;
        json = new(options);
        Write(MovementFrameObservationProtocol.OwnerFile, ownerBytes);
        published = true;
        _ = ReadInventory();
    }
    internal void Select(MovementFrameObservationSelection value)
    {
        if (selected is not null)
        { throw Invalid(); }
        var bytes = json.WriteSelection(value);
        Write(MovementFrameObservationProtocol.SelectionFile, bytes);
        selected = bytes;
    }
    internal MovementFrameObservationRecord? Read()
    {
        _ = ReadInventory();
        var path = Path.Combine(directory, MovementFrameObservationProtocol.ObservationFile);
        if (!File.Exists(path))
        { return null; }
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        if (observation is not null && !CryptographicOperations.FixedTimeEquals(observation, bytes))
        { throw Invalid(); }
        var value = json.ReadObservation(bytes);
        observation ??= bytes;
        return value;
    }
    internal void RetireSelection()
    {
        _ = ReadInventory();
        _ = Read();
        if (observation is not null)
        {
            DeleteExact(MovementFrameObservationProtocol.ObservationFile, observation);
            observation = null;
        }
        if (selected is not null)
        {
            DeleteExact(MovementFrameObservationProtocol.SelectionFile, selected);
            selected = null;
        }
        _ = ReadInventory();
    }
    internal void DeleteOwner()
    {
        if (deleted)
        { return; }
        _ = ReadInventory();
        if (selected is not null || observation is not null)
        { throw Invalid(); }
        DeleteExact(MovementFrameObservationProtocol.OwnerFile, ownerBytes);
        Directory.Delete(directory, recursive: false);
        deleted = true;
    }
    internal void PreserveTo(string destination)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
        _ = ReadInventory();
        _ = Read();
        if (Directory.Exists(destination) || File.Exists(destination))
        { throw Invalid(); }
        Directory.CreateDirectory(destination, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
        var proof = new MovementFrameObservationNodeFiles(destination, ownerBytes, options);
        if (selected is not null)
        {
            proof.Write(MovementFrameObservationProtocol.SelectionFile, selected);
            proof.selected = selected.ToArray();
        }
        if (observation is not null)
        {
            proof.Write(MovementFrameObservationProtocol.ObservationFile, observation);
            proof.observation = observation.ToArray();
        }
        _ = proof.ReadInventory();
    }
    internal byte[] ObservationBytes => observation?.ToArray() ?? throw Invalid();
    private void DeleteExact(string name, byte[] expected)
    {
        var actual = RequestCqrsProbeFiles.ReadRecord(Path.Combine(directory, name), options);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        { throw Invalid(); }
        File.Delete(Path.Combine(directory, name));
    }
    private void Write(string name, byte[] bytes)
        => RequestCqrsProbeAtomicFiles.Write(directory, Path.Combine(directory, name), bytes, options, ReadInventory);
    private RequestCqrsProbeSnapshot ReadInventory()
    {
        RequestCqrsProbeFileValidation.ValidateDirectory(directory);
        var entries = Directory.EnumerateFileSystemEntries(directory)
            .Take(options.Value.MaximumFiles + MovementFrameObservationFixtureProtocol.InventoryOverflowStep).ToArray();
        if (entries.Length > options.Value.MaximumFiles)
        { throw Invalid(); }
        var total = MovementFrameObservationFixtureProtocol.InitialAggregateBytes;
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            if (name is not (MovementFrameObservationProtocol.OwnerFile or MovementFrameObservationProtocol.SelectionFile
                or MovementFrameObservationProtocol.ObservationFile) && !RequestCqrsProbeFileValidation.IsTemporaryName(name))
            { throw Invalid(); }
            var length = ReadLength(path, name);
            if (length > options.Value.MaximumRecordBytes)
            { throw Invalid(); }
            total = checked(total + length);
            if (total > options.Value.MaximumAggregateBytes)
            { throw Invalid(); }
        }
        if (published)
        {
            var owner = RequestCqrsProbeFiles.ReadRecord(Path.Combine(directory, MovementFrameObservationProtocol.OwnerFile), options);
            if (!CryptographicOperations.FixedTimeEquals(ownerBytes, owner))
            { throw Invalid(); }
        }
        return new([], [], [], entries.Length, total);
    }
    private static long ReadLength(string path, string name)
    {
        try
        {
            var identity = OfflineRegularFile.Inspect(path);
            RequestCqrsProbePaths.RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateFileMode);
            return identity.Length;
        }
        catch (FileNotFoundException) when (RequestCqrsProbeFileValidation.IsTemporaryName(name))
        { return MovementFrameObservationFixtureProtocol.InitialAggregateBytes; }
    }
    private static InvalidOperationException Invalid() => new(MovementFrameObservationFixtureProtocol.Invalid);
}
