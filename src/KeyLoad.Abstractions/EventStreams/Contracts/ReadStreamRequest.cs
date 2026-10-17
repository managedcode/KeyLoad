namespace KeyLoad;

/// <summary>Describes a bounded read from an event stream.</summary>
/// <param name="Stream">The stream to read.</param>
/// <param name="AfterRevision">The exclusive stream revision from which to continue.</param>
/// <param name="Limit">The maximum number of records to return.</param>
/// <param name="Direction">The forward or backward order of the retained stream traversal.</param>
/// <param name="MaxBytes">The complete result byte limit; zero selects the existing configured maximum.</param>
/// <param name="Cursor">The signed original traversal continuation; AfterRevision must be zero when supplied.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadStreamRequest)]
public sealed record ReadStreamRequest([property: Orleans.Id(0)] StreamRef Stream, [property: Orleans.Id(1)] long AfterRevision = ReadStreamRequest.DefaultAfterRevision, [property: Orleans.Id(2)] int Limit = ReadStreamRequest.DefaultLimit,
    [property: Orleans.Id(3)] StreamReadDirection Direction = StreamReadDirection.Forward,
    [property: Orleans.Id(4)] int MaxBytes = ReadStreamRequest.UseConfiguredMaximum,
    [property: Orleans.Id(5)] string? Cursor = null)
{
    private const int UseConfiguredMaximum = 0;
    private const int DefaultAfterRevision = 0;
    private const int DefaultLimit = 100;
}
