namespace KeyLoad.Server.Features.Search;

/// <summary>Actual disposable native-generation boundaries for owned process recovery trials.</summary>
internal enum NativeTextFaultStage
{
    /// <summary>The physical owner receipt is durably written before native creation.</summary>
    OwnerFlushed,
    /// <summary>The first actual posting in this generation has been written.</summary>
    NativePostingWritten,
    /// <summary>Native handles settled and their complete owned inventory is durable.</summary>
    NativeInventoryFlushed,
    /// <summary>The complete source-bound manifest has been atomically published.</summary>
    ManifestPublished,
    /// <summary>The verified generation became current before obsolete generation cleanup.</summary>
    GenerationActivated
}
