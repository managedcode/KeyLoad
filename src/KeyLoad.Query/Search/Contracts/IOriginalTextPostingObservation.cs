namespace KeyLoad.Query.Features.Search;

/// <summary>Installs one original-call callback on the same native selected reader, after canonical capture settlement.</summary>
internal interface IOriginalTextPostingObservation
{
    void ObserveOriginalPosting(Func<CancellationToken, ValueTask> callback);
}
