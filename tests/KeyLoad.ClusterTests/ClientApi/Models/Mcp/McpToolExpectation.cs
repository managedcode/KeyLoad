using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.ClientApi;

/// <summary>Independent expected public schema and side-effect metadata for one frozen tool.</summary>
/// <param name="Name">The stable public tool name.</param>
/// <param name="ReadOnly">The accepted side-effect hint.</param>
/// <param name="Idempotent">The accepted identical-retry hint.</param>
/// <param name="Destructive">The accepted removal or consumption hint.</param>
/// <param name="Body">The accepted actual argument root type.</param>
/// <param name="OuterCommandId">Whether the header-command adapter has a required outer GUID.</param>
/// <param name="BodyFields">Required canonical constructor fields, excluding optional defaults.</param>
/// <param name="ResultFields">Canonical result fields asserted when the expected result is an object.</param>
/// <param name="BodyPropertyFields"></param>
internal sealed record McpToolExpectation(string Name, bool ReadOnly, bool Idempotent, bool Destructive,
    McpExpectedBody Body, bool OuterCommandId, ImmutableArray<string> BodyFields,
    ImmutableArray<string> ResultFields = default, ImmutableArray<string> BodyPropertyFields = default);
