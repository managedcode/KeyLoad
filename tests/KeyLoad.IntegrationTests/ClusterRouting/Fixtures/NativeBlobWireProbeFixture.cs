using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class NativeBlobWireProbeFixture
{
    private readonly NativeBlobWireFiles files;
    private readonly NativeBlobWireShape shape;
    private byte[]? ownerBytes;
    private byte[]? armsBytes;
    private readonly int slots;
    private bool stopped;
    private bool deleted;

    internal NativeBlobWireProbeFixture(string dataRoot, string sessionId)
    {
        Root = Path.Combine(Path.GetFullPath(dataRoot), "native-blob-wire-probes");
        SessionId = sessionId;
        if (OperatingSystem.IsWindows() || Directory.Exists(Root))
        {
            throw Invalid();
        }
        Directory.CreateDirectory(Root, RequestCqrsProbeProtocol.PrivateDirectoryMode);
        var limits = Options.Create(new RequestProbeExecutionOptions());
        limits.Value.Validate();
        slots = limits.Value.MaximumActiveGates;
        Codec = new(limits);
        shape = new(limits);
        files = new(Root, limits);
        var failures = new List<Exception>();
        try
        {
            using var publication = new FileStream(Path.Combine(Root, NativeBlobWireProbeProtocol.PublicationFile),
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = RequestCqrsProbeProtocol.PrivateFileMode,
                    Options = FileOptions.WriteThrough
                });
            global::KeyLoad.Server.ServerFailureObserver.Observe(() => publication.Flush(flushToDisk: true), failures);
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    internal string Root
    {
        get;
    }
    internal string SessionId
    {
        get;
    }
    internal NativeBlobWireCodec Codec
    {
        get;
    }
    internal NativeBlobWireOwner Owner { get; private set; } = null!;
    internal NativeBlobWireArms Arms { get; private set; } = null!;

    internal async Task PublishOwnerAsync(IEnumerable<ParameterResource> parameters,
        NodeEpochRf3Profile profile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (stopped)
        {
            throw Invalid();
        }
        var resources = parameters.ToArray();
        var physical = await ReadGuidAsync(resources, "membership-physical-b", token).ConfigureAwait(false);
        var incarnation = await ReadGuidAsync(resources, "membership-incarnation-b", token).ConfigureAwait(false);
        var source = new NativeBlobWireOwnerTuple(profile.PhysicalShardId, profile.Incarnation, Voters(0));
        var owner = new NativeBlobWireOwner(NativeBlobWireProbeProtocol.Version, SessionId, "keyload-" + profile.Incarnation.ToString("N"),
            source, source, new(physical, incarnation, Voters(TwoRf3MembershipProtocol.MembersPerGroup)));
        shape.RequireOwner(owner);
        var bytes = Codec.Encode(owner);
        if (ownerBytes is not null)
        {
            if (!ownerBytes.AsSpan().SequenceEqual(bytes))
            {
                throw Invalid();
            }
            RequireExact(NativeBlobWireProbeProtocol.OwnerFile, ownerBytes);
            return;
        }
        Owner = owner;
        files.WithPublication(() => files.WriteLocked(NativeBlobWireProbeProtocol.OwnerFile, bytes));
        ownerBytes = bytes;
    }

    internal async Task ArmAsync(TwoRf3MembershipWave wave, RemoteTransferNativeParentSeed seed,
        NativeBlobWireEffectPlan plan, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (stopped || armsBytes is not null || ownerBytes is null || wave.queryControls?.SessionId != SessionId)
        {
            throw Invalid();
        }
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
        var expected = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token).ConfigureAwait(false);
        var target = expected.Owners.Single(entry => entry.Owner.PhysicalShardId != expected.ControlOwner.PhysicalShardId).Owner;
        if (!NativeBlobWireShape.Same(Owner.SourceOwner, expected.ControlOwner)
            || !NativeBlobWireShape.Same(Owner.DestinationOwner, target) || seed.Request.DestinationPhysicalShardId != target.PhysicalShardId)
        {
            throw Invalid();
        }
        var commands = seed.BlobWireSeedCommands ?? throw Invalid();
        Arms = NativeBlobWireArmsFactory.Create(Owner, NativeBlobWireCodec.Digest(ownerBytes), seed, commands, plan);
        shape.RequireArms(Owner, NativeBlobWireCodec.Digest(ownerBytes), Arms);
        var bytes = Codec.Encode(Arms);
        files.WithPublication(() =>
        {
            RequireExact(NativeBlobWireProbeProtocol.OwnerFile, ownerBytes);
            files.WriteLocked(NativeBlobWireProbeProtocol.ArmsFile, bytes);
        });
        armsBytes = bytes;
    }

    internal byte[] Read(string name)
    {
        if (ownerBytes is null || armsBytes is null || deleted)
        {
            throw Invalid();
        }
        RequireExact(NativeBlobWireProbeProtocol.OwnerFile, ownerBytes);
        RequireExact(NativeBlobWireProbeProtocol.ArmsFile, armsBytes);
        return files.Read(name);
    }
    internal bool Exists(string name) => files.Exists(name);
    internal void StopAdmission() => stopped = true;

    internal void DeleteAfterJoined(TwoRf3MembershipWave wave)
    {
        stopped = true;
        if (!wave.applicationDisposed || !wave.nodeLocksReleased)
        {
            throw Invalid();
        }
        wave.AssertAllNodeLocksReleased();
        if (deleted)
        {
            return;
        }
        if (ownerBytes is not null)
        {
            RequireExact(NativeBlobWireProbeProtocol.OwnerFile, ownerBytes);
        }
        if (armsBytes is not null)
        {
            RequireExact(NativeBlobWireProbeProtocol.ArmsFile, armsBytes);
        }
        for (var slot = 0; slot < slots; slot++)
        {
            if (files.Exists(NativeBlobWireNames.Slot(slot)))
            {
                throw Invalid();
            }
        }
        files.WithPublication(() => { });
        Directory.Delete(Root, recursive: true);
        deleted = true;
    }

    private void RequireExact(string name, byte[] expected)
    {
        if (!files.Read(name).AsSpan().SequenceEqual(expected))
        {
            throw Invalid();
        }
    }
    private static string[] Voters(int offset)
        => TwoRf3MembershipProtocol.Nodes.Skip(offset).Take(TwoRf3MembershipProtocol.MembersPerGroup)
            .Select(node => "http://" + node + ":8080").ToArray();
    private static async Task<Guid> ReadGuidAsync(IEnumerable<ParameterResource> parameters, string name, CancellationToken token)
        => Guid.ParseExact(await parameters.Single(resource => resource.Name == name).GetValueAsync(token).ConfigureAwait(false)
            ?? throw Invalid(), "D");
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidOwner);
}
