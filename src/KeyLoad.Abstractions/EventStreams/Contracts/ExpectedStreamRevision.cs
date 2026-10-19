namespace KeyLoad;

/// <summary>Describes the expected stream state for an append operation.</summary>
/// <param name="State">Describes the expected stream state.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ExpectedStreamRevision)]
public sealed record ExpectedStreamRevision([property: Orleans.Id(0)] ExpectedStreamState State, [property: Orleans.Id(1)] long Revision = ExpectedStreamRevision.DefaultRevision)
{
    private const int DefaultRevision = 0;

    /// <summary>Creates an exact-revision stream precondition.</summary>
    /// <returns>The requested value.</returns>
    public static ExpectedStreamRevision Exact(long revision) => new(ExpectedStreamState.Exact, revision);
    /// <summary>Gets a precondition requiring the stream to be absent.</summary>
    public static ExpectedStreamRevision NoStream { get; } = new(ExpectedStreamState.NoStream);
    /// <summary>Gets a precondition accepting any current stream state.</summary>
    public static ExpectedStreamRevision Any { get; } = new(ExpectedStreamState.Any);
}
