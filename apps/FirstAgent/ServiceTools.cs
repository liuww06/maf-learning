using System.ComponentModel;
using System.Text.Json;

internal class ServiceTools
{
    private readonly ToolCallBudget _budget;

    public ServiceTools(ToolCallBudget budget)
    {
        _budget = budget;
    }

    [Description("查询学习环境中指定服务的虚构健康快照。不是实时查询。返回状态、观测时间和证据编号。")]
    public JsonElement GetServiceHealth(
        [Description("服务 ID，例如 order-api 或 inventory-api。按用户指定 ID 查询，未知 ID 不得替换。")]
        string serviceId)
    {
        Console.Error.WriteLine("[tool-call] GetServiceHealth: "
            + JsonSerializer.Serialize(new { serviceId }));

        if (!_budget.TryConsume())
        {
            return Report(new
            {
                ok = false,
                errorCode = "TOOL_BUDGET_EXCEEDED",
                serviceId,
                maxToolExecutions = _budget.Limit,
                synthetic = true,
            });
        }

        if (string.IsNullOrWhiteSpace(serviceId))
        {
            return Report(new
            {
                ok = false,
                errorCode = "INVALID_ARGUMENT",
                synthetic = true,
            });
        }

        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "service-snapshot.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        foreach (JsonElement service in root.GetProperty("services").EnumerateArray())
        {
            if (string.Equals(service.GetProperty("serviceId").GetString(), serviceId, StringComparison.Ordinal))
            {
                return Report(new
                {
                    ok = true,
                    serviceId,
                    status = service.GetProperty("status").GetString(),
                    observedAt = root.GetProperty("observedAt").GetString(),
                    evidenceId = service.GetProperty("evidenceId").GetString(),
                    environment = root.GetProperty("environment").GetString(),
                    synthetic = root.GetProperty("synthetic").GetBoolean()
                });
            }
        }

        return Report(new { ok = false, errorCode = "UNKNOWN_SERVICE", serviceId, synthetic = true });
    }

    private JsonElement Report(object result)
    {
        var json = JsonSerializer.SerializeToElement(result);
        Console.Error.WriteLine("[tool-result] " + json.GetRawText());
        return json;
    }
}
