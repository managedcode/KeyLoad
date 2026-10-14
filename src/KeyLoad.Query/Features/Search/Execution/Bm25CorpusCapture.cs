using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.Search;

internal static class Bm25CorpusCapture
{
    internal static ReplicatedOperation Create(DatabaseEngine database, string administratorId,
        string subjectId, SearchRequest request, Guid commandId, Guid? expectedGeneration,
        DateTimeOffset originalExpiry, ReadExecutionBudget work, IOptions<QueryExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        execution.Validate();
        SearchRequestValidation.Validate(request, database.Limits, false, execution);
        if (request.Text is null || request.TextField is null || request.Vector is not null || commandId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, Bm25CorpusProtocol.Invalid);
        }
        work.ConstrainLifetime(originalExpiry);
        var phase = database.WithQueryView(subjectId, request.Partition, request.Collection,
            (raw, subject, resource) =>
            {
                var view = work.CreateView(raw);
                var administrator = database.Principal(view, administratorId, database.EvaluationClock.GetUtcNow());
                if (!administrator.ClusterAdministrator)
                {
                    throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
                }
                database.Authorization.RequireFieldUse(subject, resource, request.TextField);
                var ranker = TextRanker.ForStatistics(request.Text, request.TextField, work,
                    execution.TextBudgetCheckInterval, execution.MaximumDocumentWords, execution.MaximumWordCharacters);
                database.VisitVisibleDocuments(raw, subject, request.Partition, request.Collection, work, ranker.Visit);
                return database.CaptureBm25CorpusMetadata(raw, subject, resource, request, ranker.CaptureStatistics(),
                    TextProjectionProtocol.TokenizerVersion, commandId, expectedGeneration, originalExpiry, work);
            });
        return database.CreateVerifiedBm25CorpusPublication(administratorId, phase, work);
    }
}
