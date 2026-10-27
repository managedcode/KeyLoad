using System.Runtime.CompilerServices;

namespace KeyLoad.Comparisons.Targets;

internal static class InfluxDocumentStream
{
    private const string Rejected = "InfluxReadbackRequestRejected";
    private const string TooManyRows = "InfluxReadbackRecordLimitExceeded";
    private const string StatusSeparator = ":";
    private const int ErrorCharacters = 512;

    internal static async IAsyncEnumerable<FoundDocument> ReadAsync(HttpClient http, HttpRequestMessage request,
        ComparisonExecutionOptions policy, [EnumeratorCancellation] CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(policy.OperationTimeout);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await ComparisonResponse.ReadTextAsync(response.Content, policy, deadline.Token).ConfigureAwait(false);
            var detail = new string(body.Take(ErrorCharacters).Where(character => char.IsAscii(character) && !char.IsControl(character)).ToArray());
            throw new ComparisonFailureException(Rejected + StatusSeparator + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + StatusSeparator + detail);
        }
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        var count = 0;
        var maximum = checked(DocumentComparisonContract.Current.Ingestion.Records + DocumentComparisonContract.Current.Operations);
        await foreach (var row in InfluxJsonLines.ReadAsync(input, policy, deadline).ConfigureAwait(false))
        {
            if (++count > maximum)
            {
                throw new ComparisonFailureException(TooManyRows);
            }
            yield return row;
        }
    }
}
