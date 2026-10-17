namespace KeyLoad.Server.Features.BlobStorage;

/// <summary>Optional private diagnostic owner; never database authorization or placement.</summary>
internal sealed record NativeBlobWireProbeOptions
{
    public string? Root
    {
        get; init;
    }
    public string? SessionId
    {
        get; init;
    }
}
