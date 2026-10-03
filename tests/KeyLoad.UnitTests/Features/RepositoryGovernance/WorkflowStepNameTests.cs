using System.Text;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowStepNameTests
{
    private const string NameKey = "name:";
    private const string StepsKey = "steps:";
    private const string StepMarker = "- ";
    private const string JobNamePrefix = "    name:";
    private const string CompositeDirectory = ".github/workflows/Features/BenchmarkComparisons";
    private const string ActionFile = "action.yml";

    [Test]
    [Arguments("ci.yml")]
    [Arguments("benchmarks.yml")]
    [Arguments("release.yml")]
    public async Task AcUb005EveryWorkflowJobAndStepExplainsItsAction(string fileName)
    {
        var workflow = WorkflowLayoutSource.Read(fileName);
        var jobs = WorkflowLayoutSource.JobIds(workflow);
        await Assert.That(jobs.Length).IsGreaterThan(0);
        foreach (var jobId in jobs)
        {
            var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
            var name = PropertyValue(job, JobNamePrefix);
            await Assert.That(IsReadableName(name)).IsTrue();
            await Assert.That(name == jobId).IsFalse();
            await AssertStepNames(job);
        }
    }

    [Test]
    [Arguments("IsolatedCellSetup")]
    [Arguments("IsolatedCellTeardown")]
    [Arguments("BuildIsolatedSite")]
    [Arguments("QualifySite")]
    [Arguments("DeploySite")]
    public async Task AcUb005EveryUsedCompositeStepExplainsItsAction(string actionName)
    {
        var root = IsolatedAggregateNodeProcess.RepositoryRoot();
        var action = File.ReadAllText(Path.Combine(root, CompositeDirectory, actionName, ActionFile));
        await AssertStepNames(action);
    }

    internal static string[] StepBlocks(string source)
    {
        var lines = source.Split('\n');
        var heading = lines.First(line => line.Trim() == StepsKey);
        var indent = heading.TakeWhile(char.IsWhiteSpace).Count() + StepMarker.Length;
        var marker = new string(' ', indent) + StepMarker;
        var blocks = new List<string>();
        var block = new StringBuilder();
        foreach (var line in lines.Skip(Array.IndexOf(lines, heading) + 1))
        {
            if (line.StartsWith(marker, StringComparison.Ordinal))
            {
                AddBlock(blocks, block);
            }

            if (block.Length > 0 || line.StartsWith(marker, StringComparison.Ordinal))
            {
                block.AppendLine(line.TrimEnd('\r'));
            }
        }

        AddBlock(blocks, block);
        return [.. blocks];
    }

    private static async Task AssertStepNames(string source)
    {
        var blocks = StepBlocks(source);
        await Assert.That(blocks.Length).IsGreaterThan(0);
        var names = blocks.Select(StepName).ToArray();
        foreach (var name in names)
        {
            await Assert.That(IsReadableName(name)).IsTrue();
        }

        await Assert.That(names.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(blocks.Length);
    }

    private static string StepName(string block)
    {
        var firstLine = block.Split('\n')[0];
        var indent = firstLine.TakeWhile(char.IsWhiteSpace).Count();
        var firstPrefix = new string(' ', indent) + StepMarker + NameKey;
        var ownPrefix = new string(' ', indent + StepMarker.Length) + NameKey;
        return firstLine.StartsWith(firstPrefix, StringComparison.Ordinal)
            ? firstLine[firstPrefix.Length..].Trim() : PropertyValue(block, ownPrefix);
    }

    private static string PropertyValue(string source, string prefix)
    {
        var line = source.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? string.Empty : line[prefix.Length..].Trim();
    }

    private static bool IsReadableName(string name)
    {
        var forbiddenPrefixes = new[] { "actions/", "./", "dotnet ", "node ", "docker ", "gh ", "${{" };
        return name.Length > 0 && char.IsLetter(name[0]) && name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1
            && !forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static void AddBlock(List<string> blocks, StringBuilder block)
    {
        if (block.Length > 0)
        {
            blocks.Add(block.ToString());
            block.Clear();
        }
    }
}
