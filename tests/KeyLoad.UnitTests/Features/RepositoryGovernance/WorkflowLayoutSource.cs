using System.Text;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal static class WorkflowLayoutSource
{
    private const string WorkflowsDirectory = ".github/workflows";
    private const string JobsKey = "jobs:";
    private const string Indent = "  ";

    internal static string Read(string fileName) => File.ReadAllText(Path.Combine(
        IsolatedAggregateNodeProcess.RepositoryRoot(), WorkflowsDirectory, fileName));

    internal static string[] TopLevelWorkflowFiles()
    {
        var directory = Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), WorkflowsDirectory);
        return Directory.GetFiles(directory, "*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    internal static string[] JobIds(string workflow)
    {
        var jobs = new List<string>();
        var inJobs = false;
        foreach (var line in workflow.Split('\n'))
        {
            if (!inJobs)
            {
                inJobs = line.TrimEnd('\r') == JobsKey;
                continue;
            }

            if (IsRootHeading(line))
            {
                break;
            }

            if (IsJobHeading(line))
            {
                jobs.Add(JobId(line));
            }
        }

        return [.. jobs];
    }

    internal static string JobBlock(string workflow, string jobId)
    {
        var block = ReadJobBlock(workflow, jobId);
        foreach (var (property, anchor) in new[] { ("env", "database-environment"), ("steps", "database-steps") })
        {
            var alias = "    " + property + ": *" + anchor;
            if (block.Contains(alias, StringComparison.Ordinal))
            {
                var owner = ReadJobBlock(workflow, "comparison-keyload");
                block = block.Replace(alias, ReadDatabaseAnchor(owner, property, anchor), StringComparison.Ordinal);
            }

            block = block.Replace("    " + property + ": &" + anchor, "    " + property + ":", StringComparison.Ordinal);
        }

        return block;
    }

    private static string ReadJobBlock(string workflow, string jobId)
    {
        var output = new StringBuilder();
        var inJobs = false;
        var inJob = false;
        foreach (var rawLine in workflow.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!inJobs)
            {
                inJobs = line == JobsKey;
                continue;
            }

            if (IsRootHeading(line))
            {
                break;
            }

            if (IsJobHeading(line))
            {
                if (inJob)
                {
                    break;
                }

                inJob = JobId(line) == jobId;
            }

            if (inJob)
            {
                output.AppendLine(line);
            }
        }

        return output.ToString();
    }

    private static string ReadDatabaseAnchor(string owner, string property, string anchor)
    {
        var lines = owner.Split('\n');
        var heading = "    " + property + ": &" + anchor;
        var index = Array.IndexOf(lines, heading);
        if (index < 0 || lines.Count(line => line == heading) != 1)
        {
            throw new InvalidOperationException("The database job anchor is missing or ambiguous.");
        }

        var output = new StringBuilder("    " + property + ":");
        foreach (var line in lines.Skip(index + 1))
        {
            if (line.Length > 0 && !string.IsNullOrWhiteSpace(line)
                && line.TakeWhile(char.IsWhiteSpace).Count() <= 4)
            {
                break;
            }

            output.Append('\n').Append(line);
        }

        return output.ToString().TrimEnd('\n');
    }

    internal static string EventBlock(string workflow)
    {
        var start = workflow.IndexOf("on:\n", StringComparison.Ordinal);
        var end = workflow.IndexOf("\njobs:\n", StringComparison.Ordinal);
        return start >= 0 && end > start ? workflow[start..end] : string.Empty;
    }

    private static bool IsRootHeading(string line) => line.Length > 0 && !char.IsWhiteSpace(line[0]);

    private static bool IsJobHeading(string line) => line.StartsWith(Indent, StringComparison.Ordinal)
        && line.Length > Indent.Length && !char.IsWhiteSpace(line[Indent.Length]) && line.EndsWith(':');

    private static string JobId(string line) => line[Indent.Length..^1];
}
