namespace KeyLoad.Core;

/// <summary>Requests a conditional update to one cluster membership row.</summary>
/// <param name="Key">Stable membership row key.</param>
/// <param name="ExpectedVersion">Required current row version.</param>
/// <param name="Json">Canonical membership payload.</param>
public sealed record MembershipMutation(string Key, long ExpectedVersion, string Json);
/// <summary>Stores the versioned payload of one cluster membership row.</summary>
/// <param name="Version">Committed row version.</param>
/// <param name="Json">Canonical membership payload.</param>
public sealed record MembershipRecord(long Version, string Json);
