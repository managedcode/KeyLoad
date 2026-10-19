namespace KeyLoad.Server;

internal sealed record DatabaseAuthenticationClaimSnapshot(string Type, string ValueType, string Issuer,
    string OriginalIssuer, int PropertyCount, bool MatchesSubject);
