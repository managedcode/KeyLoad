using KeyLoad.Server;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal sealed class NativeBlobWireFileFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "keyload-blob-files-" + Guid.NewGuid().ToString("N"));
    internal NativeBlobWireFiles Files { get; }
    internal NativeBlobWireCodec Codec { get; }
    internal NativeBlobWireReservations Reservations { get; }
    internal NativeBlobWireOwner Owner { get; }
    internal string OwnerDigest { get; }

    internal NativeBlobWireFileFixture()
    {
        var limits = Options.Create(new RequestProbeExecutionOptions());
        limits.Value.Validate();
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException("Private native owner files require Unix modes."); }
        Directory.CreateDirectory(Root, RequestCqrsProbeProtocol.PrivateDirectoryMode);
        try
        {
            Files = new(Root, limits);
            Codec = new(limits);
            Reservations = new(Files, Codec, limits);
            var tuple = new NativeBlobWireOwnerTuple(Guid.NewGuid(), Guid.NewGuid(), ["a", "b", "c"]);
            Owner = new(NativeBlobWireProbeProtocol.Version, Guid.NewGuid().ToString("N"), "file-unit-owner", tuple, tuple, tuple);
            OwnerDigest = NativeBlobWireCodec.Digest(Codec.Encode(Owner));
            using var publication = new FileStream(Path.Combine(Root, NativeBlobWireProbeProtocol.PublicationFile),
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = RequestCqrsProbeProtocol.PrivateFileMode,
                    Options = FileOptions.WriteThrough
                });
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => Directory.Delete(Root, recursive: true), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal void Publish(string name, byte[] bytes) => Files.WithPublication(() => Files.WriteLocked(name, bytes));
    internal static string Temporary() => RequestCqrsProbeProtocol.TemporaryFilePrefix + Guid.NewGuid().ToString("N") + ".tmp";

    internal (NativeBlobWireClaim Claim, string Digest) PublishClaim(NativeBlobWireIdentity identity)
    {
        var claim = new NativeBlobWireClaim(NativeBlobWireProbeProtocol.Version, Owner.SessionId, OwnerDigest,
            new string('a', 64), Guid.NewGuid(), identity, "metadata-only-voter", "metadata-only-full-address",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"), TimeProvider.System.GetUtcNow().AddMinutes(1), new string('b', 64));
        var bytes = Codec.Encode(claim);
        Publish(NativeBlobWireNames.Claim(identity), bytes);
        return (claim, NativeBlobWireCodec.Digest(bytes));
    }

    internal static async Task RunOwnedAsync(Func<NativeBlobWireFileFixture, Task> body)
    {
        var failures = new List<Exception>();
        NativeBlobWireFileFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => body(fixture), failures);
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose()
    {
        Files.WithPublication(static () => { });
        Directory.Delete(Root, recursive: true);
    }
}
