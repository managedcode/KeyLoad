using KeyLoad.Core;
using KeyLoad.Core.Search;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Search;

internal static class Bm25CorpusCapture
{
    internal static Command<DatabaseCommand> Create(DatabaseEngine database, string administratorId,
        string subjectId, SearchRequest request, Guid commandId, Guid? expectedGeneration,
        ReadExecutionBudget work, IOptions<QueryExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        execution.Validate();
        SearchRequestValidation.Validate(request, false, execution);
        if (request.Text is null || request.TextField is null || request.Vector is not null || commandId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, Bm25CorpusProtocol.Invalid);
        }
        var phase = database.WithQueryView(subjectId, request.Partition, request.Collection, Capability.None,
            (raw, subject, resource) =>
            {
                database.Authorization.RequireFieldUse(subject, resource, request.TextField);
                var ranker = TextRanker.ForStatistics(request.Text, request.TextField, work,
                    execution.TextBudgetCheckInterval, execution.MaximumDocumentWords, execution.MaximumWordCharacters);
                database.VisitVisibleDocuments(raw, subject, request.Partition, request.Collection, work, ranker.Visit);
                return database.CaptureBm25CorpusMetadata(raw, subject, resource, request, ranker.CaptureStatistics(),
                    TextProjectionProtocol.TokenizerVersion, commandId, expectedGeneration, work);
            }, work, metadata =>
            {
                var administrator = database.Principal(metadata, administratorId, database.EvaluationClock.GetUtcNow());
                if (!administrator.ClusterAdministrator)
                {
                    throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
                }
            });
        return database.CreateVerifiedBm25CorpusPublication(administratorId, phase, work);
    }
}
