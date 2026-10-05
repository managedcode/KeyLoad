using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Independent expected public schema and side-effect metadata for one frozen tool.</summary>
/// <param name="Name">The stable public tool name.</param>
/// <param name="ReadOnly">The accepted side-effect hint.</param>
/// <param name="Idempotent">The accepted identical-retry hint.</param>
/// <param name="Destructive">The accepted removal or consumption hint.</param>
/// <param name="Body">The accepted actual argument root type.</param>
/// <param name="OuterCommandId">Whether the header-command adapter has a required outer GUID.</param>
/// <param name="BodyFields">Required canonical constructor fields, excluding optional defaults.</param>
internal sealed record McpToolExpectation(string Name, bool ReadOnly, bool Idempotent, bool Destructive,
    McpExpectedBody Body, bool OuterCommandId, ImmutableArray<string> BodyFields,
    ImmutableArray<string> ResultFields = default);

/// <summary>The public canonical request root, independent of any native schema implementation.</summary>
internal enum McpExpectedBody
{
    /// <summary>The tool accepts no arguments.</summary>
    None,
    /// <summary>The tool requires its actual canonical object request.</summary>
    Object,
    /// <summary>The dispatch control tool requires a boolean request.</summary>
    Boolean
}
