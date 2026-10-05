using System.Security.Cryptography;
using System.Text;

namespace KeyLoad;

/// <summary>Creates stable identifiers for idempotent physical-shard catalog bootstrap.</summary>
public static class PhysicalShardCatalogIdentity
{
    private const string BootstrapCommandDomain = "KeyLoad.PhysicalShardCatalog.Bootstrap.v1";
    private const string EmptyIdentity = "The physical shard identity is empty.";
    private const string DomainTerminator = "\0";
    private const int GuidBytes = 16;
    private static readonly byte[] DomainBytes = Encoding.ASCII.GetBytes(BootstrapCommandDomain + DomainTerminator);

    /// <summary>Derives the stable bootstrap command identity from the explicit shard identity.</summary>
    /// <param name="physicalShardId">The configured, opaque shard identifier.</param>
    /// <returns>The big-endian Guid represented by the first 16 bytes of the frozen SHA-256 digest.</returns>
    /// <exception cref="ArgumentException">The shard identifier is empty.</exception>
    public static Guid CreateBootstrapCommandId(Guid physicalShardId)
    {
        if (physicalShardId == Guid.Empty)
        { throw new ArgumentException(EmptyIdentity, nameof(physicalShardId)); }
        Span<byte> input = stackalloc byte[DomainBytes.Length + GuidBytes];
        DomainBytes.AsSpan().CopyTo(input);
        physicalShardId.TryWriteBytes(input[DomainBytes.Length..], bigEndian: true, out _);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, digest);
        return new Guid(digest[..GuidBytes], bigEndian: true);
    }
}
