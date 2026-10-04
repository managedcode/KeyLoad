namespace KeyLoad.Server;

/// <summary>Counts actual bounded JSON tokens and names before native DOM allocation.</summary>
/// <param name="TokenCount">The total JSON reader tokens, including container delimiters.</param>
/// <param name="PropertyCount">The total object property names.</param>
/// <param name="Depth">The maximum container nesting, zero for scalar values or count-only projections.</param>
internal readonly record struct McpFrameShape(int TokenCount, int PropertyCount, int Depth = 0);
