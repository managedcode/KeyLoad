namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-030 and AC-CQ-031: native string discrimination requires named constants.</summary>
internal sealed class MagicRuntimeStringComparisonTests
{
    [Test]
    [Arguments("value == [|\"ready\"|]")]
    [Arguments("value == [|\"\"|]")]
    [Arguments("value == [|@\"ready\"|]")]
    [Arguments("value == [|\"ready\\n\"|]")]
    [Arguments("[|\"ready\"|] != value")]
    [Arguments("value is [|\"ready\"|]")]
    [Arguments("value is not [|\"ready\"|]")]
    [Arguments("value switch { [|\"ready\"|] => true, _ => false }")]
    public async Task NativeEqualityAndConstantPatternsRejectStringTokensAsync(string expression)
    {
        var source = $$"""
            internal static class Subject
            {
                internal static bool Execute(string value) => {{expression}};
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    [Arguments("value.Equals([|\"ready\"|], StringComparison.Ordinal)")]
    [Arguments("[|\"ready\"|].Equals(value, StringComparison.Ordinal)")]
    [Arguments("NativeString.Equals(value, [|\"ready\"|], StringComparison.Ordinal)")]
    [Arguments("NativeString.Compare(value, [|\"next\"|], StringComparison.Ordinal)")]
    [Arguments("NativeString.CompareOrdinal(value, [|\"last\"|])")]
    [Arguments("value.StartsWith([|\"prefix\"|], StringComparison.Ordinal)")]
    [Arguments("value.EndsWith([|\"suffix\"|], StringComparison.Ordinal)")]
    [Arguments("value.Contains([|\"token\"|], StringComparison.Ordinal)")]
    [Arguments("value.IndexOf([|\"entry\"|], StringComparison.Ordinal)")]
    [Arguments("value.LastIndexOf([|\"entry\"|], StringComparison.Ordinal)")]
    [Arguments("value.IndexOf(value: [|\"entry\"|], startIndex: 0, count: 1, comparisonType: StringComparison.Ordinal)")]
    public async Task NativeComparisonOverloadsAndAliasesRejectInlinePolicyAsync(string expression)
    {
        var source = $$"""
            using System;
            using NativeString = System.String;
            internal static class Subject
            {
                internal static object Execute(string value) => {{expression}};
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task TraditionalStringSwitchRejectsEachFullStringTokenAsync()
    {
        const string source = """
            internal static class Subject
            {
                internal static int Execute(string value)
                {
                    switch (value)
                    {
                        case [|"ready"|]: return 1;
                        case [|"pending"|]: return 0;
                        default: return -1;
                    }
                }
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task ConstDiscriminatorsPreserveEqualityMethodsAndSwitchPatternsAsync()
    {
        const string source = """
            using System;
            internal static class Subject
            {
                private const string ReadyState = "ready";
                private const string QueuePrefix = "queue/";
                internal static bool Execute(string value)
                {
                    var ready = value == ReadyState;
                    var prefix = value.StartsWith(QueuePrefix, StringComparison.Ordinal);
                    var match = value is ReadyState;
                    var selected = value switch { ReadyState => true, _ => false };
                    return ready && prefix && match && selected;
                }
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task RuntimeCallerTextAndApplicationMethodsRequireGeneralTextIdentitiesAsync()
    {
        const string source = """
            internal sealed class SearchInput
            {
                internal string Contains(string userText) => userText;
                internal string StartsWith(string userText) => userText;
                internal string Compare(string userText) => userText;
                internal string ToString(string userText) => userText;
                internal static string Send(string userText) => userText;
                internal string Execute() =>
                    Contains([|"caller text"|]) + StartsWith([|"caller prefix"|]) +
                    Compare([|"caller comparison"|]) + ToString([|"caller format"|]) + Send([|"caller payload"|]);
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task ObjectEqualityAndCharDiscriminationRequireGeneralTextIdentitiesAsync()
    {
        const string source = """
            internal static class Subject
            {
                internal static bool Execute(object identity, char token) =>
                    identity == [|"caller data"|] || token == [|'x'|] || token is [|'y'|];
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }
}
