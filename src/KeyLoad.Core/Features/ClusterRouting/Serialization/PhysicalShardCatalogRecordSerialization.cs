using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

/// <summary>Owns the catalog's native record key and ZoneTree byte decoding.</summary>
internal static class PhysicalShardCatalogRecordSerialization
{
    private const string CatalogSpace = "physical-shard-catalog";
    private const string CatalogVersion = "v1";
    private const string Malformed = "The committed physical shard catalog is malformed.";

    internal static byte[] CatalogKey() => KeyCodec.Encode(CatalogSpace, CatalogVersion);

    internal static PhysicalShardRecord InitialRecord(BootstrapPhysicalShardCatalogRequest request)
        => new(request.PhysicalShardId, request.Incarnation, request.VoterIds,
            PhysicalShardCatalogProtocol.InitialPlacementEpoch);

    internal static PhysicalShardCatalog InitialCatalog(BootstrapPhysicalShardCatalogRequest request)
        => new(PhysicalShardCatalogProtocol.CurrentVersion, PhysicalShardCatalogProtocol.InitialRevision,
            InitialRecord(request));

    internal static PhysicalShardCatalog? Read(IKeyValueView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        PhysicalShardCatalog? catalog = null;
        view.ReadValue(CatalogKey(), bytes => catalog = Deserialize(bytes));
        return catalog;
    }

    internal static PhysicalShardCatalog? Read(IKeyValueView view, ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(grant);
        PhysicalShardCatalog? catalog = null;
        grant.ReadValue(view, CatalogKey(), bytes => catalog = Deserialize(bytes));
        return catalog;
    }

    private static PhysicalShardCatalog Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > PhysicalShardCatalogProtocol.MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, Malformed);
        }

        try
        {
            return NativeSerialization.Deserialize<PhysicalShardCatalog>(bytes);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Corruption, Malformed);
        }
    }
}
