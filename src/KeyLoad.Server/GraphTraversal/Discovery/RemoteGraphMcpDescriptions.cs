namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphMcpDescriptions
{
    private const string UpsertDescription = "Upsert one authorized remote source edge and explicitly deliver its eventual incoming projection. Source, target and completion receipts have separate cuts; interrupted work can leave effects.";
    private const string DeleteDescription = "Delete one authorized remote source edge at its expected revision and explicitly deliver the eventual removal. Source, target and completion receipts are separate; this is not a cross-partition transaction.";
    private const string DeliverDescription = "Deliver the current authorized remote edge revision to one exact destination using a stable command identity. Target effect and source completion have separate durable receipts.";
    private const string IncomingDescription = "Read a bounded authorized eventual remote incoming projection. Canonical source edges and endpoint visibility are revalidated with separate source and target cuts; no global snapshot is implied.";

    internal static string? Find(string name) => name switch
    {
        RemoteGraphPublicProtocol.UpsertTool => UpsertDescription,
        RemoteGraphPublicProtocol.DeleteTool => DeleteDescription,
        RemoteGraphPublicProtocol.DeliverTool => DeliverDescription,
        RemoteGraphPublicProtocol.IncomingTool => IncomingDescription,
        _ => null
    };
}
