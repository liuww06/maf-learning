# 第 2 课 注册第一个函数工具

本小节目标：让 Agent 请求执行 C# 函数，从已有 JSON 快照读取服务状态，再根据工具结果回答。你负责把下面的三个修改接入 FirstAgent，并提交实际调用证据。

这里查询的是固定的虚构快照，不是实时生产服务。先把调用机制学清楚，后续可以替换数据访问实现。

## 三部分如何连接

`GetServiceHealth` 是业务函数，负责读取数据；`AIFunctionFactory.Create` 将函数包装成带名称、描述、参数 schema 和执行入口的工具；`ChatOptions.Tools` 将工具提供给 Agent。

`Description` 用来解释工具用途和参数含义。仅写一个函数或一段描述，不会让它自动成为当前 Agent 的可用工具。[微软函数工具文档](https://learn.microsoft.com/en-us/agent-framework/agents/tools/function-tools?pivots=programming-language-csharp)

## 第一步 将快照复制到输出目录

打开 [FirstAgent.csproj](../apps/FirstAgent/FirstAgent.csproj)，在 `Project` 内增加一个 `ItemGroup`：

```xml
<ItemGroup>
  <None Include="../../fixtures/service-snapshot.json"
        Link="fixtures/service-snapshot.json"
        CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

这样函数可以通过 `AppContext.BaseDirectory` 定位快照，不依赖启动命令时的工作目录。数据源是 [service-snapshot.json](../fixtures/service-snapshot.json)，无需手工复制或重新编造数据。

## 第二步 编写工具函数

在 `apps/FirstAgent/` 新建 `ServiceTools.cs`，先读懂再加入以下代码：

```csharp
using System.ComponentModel;
using System.Text.Json;

internal static class ServiceTools
{
    [Description("查询学习环境中指定服务的虚构健康快照。不是实时查询。返回状态、观测时间和证据编号。")]
    public static JsonElement GetServiceHealth(
        [Description("服务 ID，例如 order-api 或 inventory-api。按用户指定的 ID 查询，未知 ID 不得替换。")]
        string serviceId)
    {
        Console.Error.WriteLine("[tool-call] GetServiceHealth "
            + JsonSerializer.Serialize(new { serviceId }));

        if (string.IsNullOrWhiteSpace(serviceId))
        {
            return Report(new { ok = false, errorCode = "INVALID_ARGUMENT", synthetic = true });
        }

        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "service-snapshot.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

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

    private static JsonElement Report(object result)
    {
        JsonElement json = JsonSerializer.SerializeToElement(result);
        Console.Error.WriteLine("[tool-result] " + json.GetRawText());
        return json;
    }
}
```

注意两处设计：数据文件路径由程序确定，模型只提供 `serviceId`；找不到服务时返回 `UNKNOWN_SERVICE`，不会把“没有数据”解释为健康。这些日志只包含当前练习的虚构数据，不应照搬为打印真实业务数据的通用日志方式。

这里返回的 `ok`、`errorCode` 是我们定义的业务结果格式，MAF 不会自动赋予它们重试或权限语义。文件缺失、JSON 损坏、超时等处理将在后续小节补充。

## 第三步 注册工具并更新能力说明

在 [Program.cs](../apps/FirstAgent/Program.cs) 现有的 `new ChatOptions` 中，增加下面这一行，其他选项继续保留：

```csharp
Tools = [AIFunctionFactory.Create(ServiceTools.GetServiceHealth)],
```

文件已有 `using Microsoft.Extensions.AI;`，不需要再添加依赖包。

将 Instructions 最后一段原先禁止声称查询任何真实服务或工具的说明，调整为适合本课的边界：

```csharp
+ "说明能力边界。你可以调用 GetServiceHealth 查询虚构服务快照。"
+ "涉及服务状态时先调用工具。ok=true 时，只引用结果中实际存在的状态、观测时间和 evidenceId。"
+ "ok=false 时，报告查询未成功、服务 ID 和 errorCode，不给出服务健康结论。"
+ "errorCode 是错误码，不是证据编号。未返回的观测时间或证据编号应省略或明确说未提供，不能补造。"
+ "明确这是练习快照，不能把它说成实时生产状态。";
```

这些指令用于引导模型如何使用结果；实际查询能力来自函数实现与注册。

## 运行两个问题

从学习仓库根目录重新编译，然后运行：

```bash
dotnet build apps/FirstAgent/FirstAgent.csproj --no-restore
dotnet run --project apps/FirstAgent --no-build --no-restore -- stream "请调用工具查询 order-api 的快照状态，并给出观测时间和证据编号。"
dotnet run --project apps/FirstAgent --no-build --no-restore -- stream "请调用工具查询 billing-api 的快照状态。"
```

每个问题是一次独立运行，可能包含多次模型请求。保持当前模型不变，以便判断网关、模型和工具协议的实际行为。

| 问题 | 应看到的 C# 日志 | 回答要求 |
| --- | --- | --- |
| order-api | `[tool-call]` 参数为 order-api；`[tool-result]` 含 degraded、2026-11-06T06:00:00Z、snapshot-order-001 | 引用工具结果，明确是虚构快照 |
| billing-api | 参数仍是 billing-api；结果含 UNKNOWN_SERVICE | 说明数据集中没有该服务；不编造状态、时间或证据编号，不将错误码当作证据编号 |

模型声称“我查询了”不是执行证据；这里的 `[tool-call]` 和 `[tool-result]` 是业务函数打印的宿主日志。本小节不要求工具一定只执行一次，看到重复调用时保留次数和顺序，下一小节加入更明确的调用预算。

## 提交与复述

提交实际模型/模式、两次运行的工具日志和最终回答，不提供密钥或完整认证日志。代码修改可以直接由助手在工作区查看。

同时用一句话解释：`Description`、`AIFunctionFactory.Create`、`Tools` 各自解决什么问题？

## 验证范围

2026-11-09 已在临时项目中使用当前锁定的 MAF 1.27.0 验证注册和函数片段：编译 0 警告、0 错误；通过 `AIFunction.InvokeAsync` 验证订单、库存、未知服务和空参数四种结果；参数 schema 中 `serviceId` 为必填字符串；在仓库之外的工作目录也能定位快照。

临时验证没有改动你的应用，也没有发送模型或网关请求。确定性函数结果、工具包装和编译检查，与模型真实发起工具调用是不同层次；后续用户提交的两次日志已展示当前网关和模型的工具交互，回答中的字段语义问题记录在下方。详细证据见 [本课记录](../progress/2026-11-09-l02.md)。

本小节只覆盖注册、快照查询与执行证据。L02 的非法模型参数、未知工具、取消以及循环上限继续在后续小节实现和验证。

## 首次真实调用后的修正练习

2026-11-09 用户已提交两次工具日志和模型输出，两次均执行了正确的服务查询。billing-api 的模型回答却把 `errorCode` 字段称为“证据编号”，而工具结果根本没有 `evidenceId`。原示例无条件要求保留状态、时间和证据编号，没有区分成功与失败返回；这是需要改进的指令设计，是否造成了该次错误还需对照实验。

本任务单已将推荐指令调整为上面的成功/失败分支。请自行同步到 Program.cs，保持模型、工具返回和问题不变，重新运行两个问题进行比较。一次符合要求说明本次改善，不能证明所有输出都可靠；硬性字段一致性需要后续程序验证或直接由代码呈现关键字段。

另外，本次实现遗漏了方法上的 `Description`，目前只有参数描述。请在 `GetServiceHealth` 方法上补齐第二步代码中的方法描述。已有日志证明缺少它也可能选对这个单一工具，但方法用途应该通过明确的工具元数据表达。

完整复盘见 [工具结果与最终回答](../knowledge/03-tool-results-and-answers.md)。

同日第二轮：用户已完成条件化指令和方法描述修改，提交的两个新样本满足当前要求，错误码误标问题未重现。接下来完成 [工具执行预算](02-tool-budget.md)，以运行时代码实施次数限制。
