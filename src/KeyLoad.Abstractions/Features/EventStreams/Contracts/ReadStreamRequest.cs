namespace KeyLoad;

/// <summary>Describes a bounded read from an event stream.</summary>
/// <param name="Stream">The stream to read.</param>
/// <param name="AfterRevision">The exclusive stream revision from which to continue.</param>
/// <param name="Limit">The maximum number of records to return.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadStreamRequest)]
public sealed record ReadStreamRequest([property: Orleans.Id(0)] StreamRef Stream, [property: Orleans.Id(1)] long AfterRevision = ReadStreamRequest.DefaultAfterRevision, [property: Orleans.Id(2)] int Limit = ReadStreamRequest.DefaultLimit)
{
    private const int DefaultAfterRevision = 0;
    private const int DefaultLimit = 100;
}
