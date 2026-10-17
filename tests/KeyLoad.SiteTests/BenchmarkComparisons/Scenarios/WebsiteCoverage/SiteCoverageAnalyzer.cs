using System.Text;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageFileResult(string Path, string Sha256, int ExecutableLines, int CoveredLines,
    int BlockOutcomes, int CoveredBlockOutcomes);

internal sealed record SiteCoverageTotals(int ExecutableLines, int CoveredLines, int BlockOutcomes,
    int CoveredBlockOutcomes);
internal sealed record SiteCoverageLineCoverage(IReadOnlySet<int> Executable, IReadOnlySet<int> Covered);

internal static class SiteCoverageAnalyzer
{
    public static SiteCoverageFileResult Analyze(SiteCoverageSourceEntry source, string repository,
        IReadOnlyList<SiteCoverageScriptSnapshot> snapshots)
    {
        var lines = AnalyzeLines(source, repository, snapshots);
        var branches = SiteCoverageSnapshotResolver.CountBranches(snapshots);
        return new(source.Path, source.Sha256, lines.Executable.Count, lines.Covered.Count,
            branches.Outcomes, branches.CoveredOutcomes);
    }

    public static SiteCoverageLineCoverage AnalyzeLines(SiteCoverageSourceEntry source, string repository,
        IReadOnlyList<SiteCoverageScriptSnapshot> snapshots)
    {
        var text = File.ReadAllText(Path.Combine(repository, source.Path.Replace('/', Path.DirectorySeparatorChar)),
            new UTF8Encoding(false, true));
        var executable = LexExecutablePositions(text);
        var coveredLines = new HashSet<int>();
        var executableLines = new HashSet<int>();
        var line = SiteCoverageTokens.One;
        for (var offset = SiteCoverageTokens.Zero; offset < text.Length; offset++)
        {
            if (text[offset] is '\r' or '\n')
            {
                if (text[offset] == '\n' || offset + SiteCoverageTokens.One >= text.Length || text[offset + SiteCoverageTokens.One] != '\n')
                {
                    line++;
                }

                continue;
            }

            if (!executable[offset])
            {
                continue;
            }

            executableLines.Add(line);
            if (SiteCoverageSnapshotResolver.IsCovered(offset, snapshots))
            {
                coveredLines.Add(line);
            }
        }

        return new(executableLines, coveredLines);
    }

    public static SiteCoverageTotals Sum(IEnumerable<SiteCoverageFileResult> files) => files.Aggregate(
        new SiteCoverageTotals(SiteCoverageTokens.Zero, SiteCoverageTokens.Zero, SiteCoverageTokens.Zero,
            SiteCoverageTokens.Zero), (total, file) => new(total.ExecutableLines + file.ExecutableLines,
            total.CoveredLines + file.CoveredLines, total.BlockOutcomes + file.BlockOutcomes,
            total.CoveredBlockOutcomes + file.CoveredBlockOutcomes));

    private static bool[] LexExecutablePositions(string source)
    {
        var executable = new bool[source.Length];
        var quote = '\0';
        var lineComment = false;
        var blockComment = false;
        var escaped = false;
        for (var index = SiteCoverageTokens.Zero; index < source.Length; index++)
        {
            index += ProcessLexCharacter(source, index, executable, ref quote, ref lineComment, ref blockComment,
                ref escaped);
        }

        return executable;
    }

    private static int ProcessLexCharacter(string source, int index, bool[] executable, ref char quote,
        ref bool lineComment, ref bool blockComment, ref bool escaped)
    {
        var current = source[index];
        var next = index + SiteCoverageTokens.One < source.Length ? source[index + SiteCoverageTokens.One] : '\0';
        if (lineComment)
        {
            lineComment = current is not '\r' and not '\n';
            return SiteCoverageTokens.Zero;
        }

        if (blockComment)
        {
            return EndBlockComment(current, next, ref blockComment);
        }

        if (quote != '\0')
        {
            return ProcessQuotedCharacter(current, executable, index, ref quote, ref escaped);
        }

        return ProcessCodeCharacter(current, next, executable, index, ref lineComment, ref blockComment, ref quote);
    }

    private static int EndBlockComment(char current, char next, ref bool blockComment)
    {
        if (current == '*' && next == '/')
        {
            blockComment = false;
            return SiteCoverageTokens.One;
        }

        return SiteCoverageTokens.Zero;
    }

    private static int ProcessQuotedCharacter(char current, bool[] executable, int index, ref char quote,
        ref bool escaped)
    {
        executable[index] = !char.IsWhiteSpace(current);
        if (escaped)
        {
            escaped = false;
        }
        else if (current == '\\')
        {
            escaped = true;
        }
        else if (current == quote)
        {
            quote = '\0';
        }

        return SiteCoverageTokens.Zero;
    }

    private static int ProcessCodeCharacter(char current, char next, bool[] executable, int index,
        ref bool lineComment, ref bool blockComment, ref char quote)
    {
        if (current == '/' && next == '/')
        {
            lineComment = true;
            return SiteCoverageTokens.One;
        }

        if (current == '/' && next == '*')
        {
            blockComment = true;
            return SiteCoverageTokens.One;
        }

        if (current is '\'' or '"' or '`')
        {
            executable[index] = true;
            quote = current;
        }
        else
        {
            executable[index] = !char.IsWhiteSpace(current);
        }

        return SiteCoverageTokens.Zero;
    }

}
