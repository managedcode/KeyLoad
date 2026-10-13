namespace KeyLoad.Features.QueryExecution;

internal struct SqlDollarQuoteState
{
    internal int Start;
    internal int Offset;
    internal int DelimiterLength;
    internal int ContentStart;
    internal int ContentEnd;
}
