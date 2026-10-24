namespace KeyLoad.Query.Search;

/// <summary>Installs one original-call callback on the same execution selected reader, after canonical capture settlement.</summary>
internal interface IOriginalTextPostingObservation
{
    void ObserveOriginalPosting(Func<CancellationToken, ValueTask> callback);
}
