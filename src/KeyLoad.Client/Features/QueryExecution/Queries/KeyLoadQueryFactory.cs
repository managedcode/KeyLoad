using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

/// <summary>Creates an immutable typed query builder for one partition and collection.</summary>
public static class KeyLoadQuery
{
    private const string DefaultProjectionPath = "*";

    /// <summary>Creates a query builder with the standard all-fields projection and page limit.</summary>
    /// <typeparam name="T">Application record shape used to translate expressions.</typeparam>
    /// <param name="partition">Target partition for the query.</param>
    /// <param name="collection">Collection to query.</param>
    /// <param name="translationOptions">Centrally validated client translation budgets.</param>
    /// <returns>A builder whose initial request has no filter or ordering.</returns>
    public static KeyLoadQuery<T> From<T>(PartitionRef partition, string collection,
        IOptions<QueryTranslationOptions> translationOptions)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(translationOptions);
        var query = new SelectQuery(collection, null, [new(DefaultProjectionPath, DefaultProjectionPath)], null,
            [], translationOptions.Value.DefaultQueryLimit);
        return new(partition, query, translationOptions);
    }
}
