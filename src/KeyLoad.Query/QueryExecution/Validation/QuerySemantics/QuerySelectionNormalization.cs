using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Query;

internal static class QuerySelectionNormalization
{
    private const string QueryProjectionEntryIsMissingDetail = "A query projection entry is missing.";
    private const string QueryProjectionHasDuplicateAliasesOrAnInvalidStarDetail = "The query projection has duplicate aliases or an invalid star.";
    private const string QueryOrderingEntryIsMissingDetail = "A query ordering entry is missing.";

    internal static ImmutableArray<Selection> Projection(ImmutableArray<Selection> input)
    {
        var projection = input.Select(selection =>
        {
            if (selection is null)
            {
                throw Errors.Fail(ErrorCode.Validation, QueryProjectionEntryIsMissingDetail);
            }
            QueryValidation.Path(selection.Path, true);
            JsonData.Identifier(selection.Alias);
            if (selection.SourceAlias is not null)
            {
                JsonData.Identifier(selection.SourceAlias);
            }
            return selection with
            {
            };
        }).ToImmutableArray();
        if (projection.Select(s => s.Alias).Distinct(StringComparer.Ordinal).Count() != projection.Length
            || projection.Length > 1 && projection.Any(s => s.Path == SqlSyntax.Star))
        {
            throw Errors.Fail(ErrorCode.Validation, QueryProjectionHasDuplicateAliasesOrAnInvalidStarDetail);
        }
        return projection;
    }

    internal static ImmutableArray<Ordering> Order(ImmutableArray<Ordering> input)
        => input.Select(item =>
        {
            if (item is null)
            {
                throw Errors.Fail(ErrorCode.Validation, QueryOrderingEntryIsMissingDetail);
            }
            QueryValidation.Path(item.Path);
            return item with
            {
            };
        }).ToImmutableArray();
}
