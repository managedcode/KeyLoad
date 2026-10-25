using System.Text.Json;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateColdDropRows
{
    internal const string AdministratorId = "admin-original-cold";
    internal const string FreshId = "recreated-cold";
    internal const long Initial = 1;
    internal const long Absent = 0;
    private const int OriginalValue = 37;
    private const int FreshValue = 41;

    internal static EntityRef LinkedDocument(FunctionalGateContext context, FunctionalGateColdModel model)
        => context.Bind(new(context.Partition, FunctionalGateColdProtocol.Resource("docs", model.Index),
            FunctionalGateColdProtocol.Id));

    internal static DocumentResult Original(FunctionalGateContext context, FunctionalGateColdModel model)
        => new(new(context.Partition, model.TableDefinition.Name, FunctionalGateColdProtocol.Id), Initial,
            Json(context, model, FunctionalGateColdProtocol.Id, OriginalValue), false, []);

    internal static DocumentResult Administrator(FunctionalGateContext context, FunctionalGateColdModel model)
        => new(new(context.Partition, model.TableDefinition.Name, AdministratorId), Initial,
            Json(context, model, AdministratorId, OriginalValue), false, []);

    internal static DocumentResult Fresh(FunctionalGateContext context, FunctionalGateColdModel model)
        => new(new(context.Partition, model.TableDefinition.Name, FreshId), Initial,
            Json(context, model, FreshId, FreshValue), false, []);

    private static string Json(FunctionalGateContext context, FunctionalGateColdModel model, string id, int value)
        => JsonSerializer.Serialize(new
        {
            id,
            value,
            reference = JsonSerializer.Serialize(LinkedDocument(context, model), JsonDefaults.Options)
        }, JsonDefaults.Options);
}
