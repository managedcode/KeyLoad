namespace KeyLoad.Features.QueryExecution;

/// <summary>Scalar state retained between bounded SQL trivia chunks.</summary>
internal struct SqlTriviaState
{
    internal int BlockDepth { get; set; }
    internal bool InLineComment { get; set; }
}

/// <summary>Distinguishes bounded progress, completion and invalid comment bounds.</summary>
internal enum SqlTriviaStatus { More, Complete, UnterminatedComment, DepthLimitExceeded }

/// <summary>One lexical definition shared by SQL adapters, without database authority.</summary>
internal static class SqlTriviaSyntax
{
    internal const char Slash = '/';
    internal const char Star = '*';
    internal const char Dash = '-';
    internal const char CarriageReturn = '\r';
    internal const char LineFeed = '\n';
    internal const int PairCharacters = 2;
}
