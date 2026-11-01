namespace KeyLoad.Server;

/// <summary>A client connection's authenticated principal and the exact credential header it was proved with.</summary>
internal sealed record ServerConnectionPrincipal(string Credential, PrincipalRecord Value);
