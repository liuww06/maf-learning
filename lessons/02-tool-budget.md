# 第 2 课 工具执行预算

前置结果：[函数工具练习](02-function-tools.md) 的第二轮两个样本满足当前回答要求。本小节实现一个硬约束：单次 Agent 运行最多准许两次服务快照查询，不论模型更换服务 ID 还是重复提出调用。

ToolCallBudget 是本课自定义的业务类型，不是 MAF 内置 API。2026-11-12 本人完成核心预算实现、三组参数化检查及返回字段断言；助手收尾时补齐并发检查、四项汇总和 CI 接入。Release 编译及四项检查均通过，总退出 0。单独的新旧对象隔离检查因当前覆盖重复而取消。Used 只供近似观测，允许过时，不要求 getter 加锁；准入只依据 TryConsume。以下保留实现任务与设计依据，助手补齐部分不计为本人独立掌握。详见 [最新验收记录](../progress/2026-11-12-tool-budget.md)、[单场景验收](../progress/2026-11-11-tool-budget.md) 和 [前次审查](../progress/2026-11-10-tool-budget.md)。

## 先固定计数规则

| 项目 | 本课规则 |
| --- | --- |
| 范围 | 一次顶层 RunAsync 或 RunStreamingAsync，共享一个预算对象 |
| 限额 | 最多准许 2 次工具函数执行尝试，不是“首次加两次重试” |
| 扣减时机 | 函数收到调用后，读取快照与执行查询前，原子地检查并占用额度 |
| 业务失败 | UNKNOWN_SERVICE 也消耗一次；进入函数后的空字符串校验失败同样消耗一次 |
| 已耗尽 | 返回 TOOL_BUDGET_EXCEEDED，不读快照、不查询、不自动换服务 |
| 重置 | 新的顶层运行获得新预算；同一次运行中的新工具调用不得重置 |

这里限制的是获准执行的尝试，业务是否成功不影响扣减。参数在进入函数前就被绑定层拒绝时，不经过本课计数器；这类请求与模型轮数另行控制。后续增加工具时，同一任务中的工具需要共享预算，而不能每个工具各自拥有完整额度。

## 任务一 实现预算对象

新建 `apps/FirstAgent/ToolCallBudget.cs`，提供：

```csharp
// 自定义接口形状；需要你实现类和线程安全的状态更新。
// ToolCallBudget(int limit)
// bool TryConsume()
// int Used { get; }
// int Limit { get; }
```

构造时拒绝非正的 limit。TryConsume 在尚有额度时将 Used 增加 1 并返回 true；否则返回 false，Used 不再增加。并发调用也必须保证 `0 <= Used <= Limit`。

提示：用同一把 `lock` 保护 TryConsume 内部的“检查与增加”整个操作，不要先在锁外判断，再单独增加。计数器不能放在工具方法的局部变量中，也不能作为整个进程共享的 static 可变状态。

Used 的契约是诊断用的近似计数，允许过时，不承诺读取时的最新值或刷新时限；外部读取不必为本课加锁。即使 getter 加锁，返回后额度仍可能被别的线程消费，不能据此预先判定后续消费成功。只有 TryConsume 返回 true 才表示已占用额度；也不要用未同步的 Used 轮询决定任务何时结束。

这里使用 int，其单次读写具有原子性，但不意味着检查与递增组成的操作天然原子。依据见 [C# 规范 9.6](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/variables#96-atomicity-of-variable-references)。如果以后需要更强的观测语义，应重新定义 Used 的同步契约；性能影响需按调用频率与竞争程度测量。

## 任务二 在工具入口实施限制

将 ServiceTools 从静态类改成实例类，通过构造函数保存 ToolCallBudget。GetServiceHealth 改成实例方法，保留现有方法和参数 Description；Report 可继续保持静态。

函数入口按以下顺序组织：

```text
[tool-call] 记录本次调用请求
    → TryConsume
        → false：记录 [tool-rejected]，返回超限结果
        → true：记录 [tool-admitted]，再执行原有校验与快照查询
```

超限结果使用本项目自定义格式：

```json
{
  "ok": false,
  "errorCode": "TOOL_BUDGET_EXCEEDED",
  "serviceId": "order-api",
  "maxToolExecutions": 2,
  "synthetic": true
}
```

用已有 Report 方法返回 JSON。不要添加健康状态或证据编号；拒绝执行不能证明服务健康情况。

本人已将错误码统一为 `TOOL_BUDGET_EXCEEDED`，补齐 serviceId 与 maxToolExecutions，并将 `[tool-call]` 移到准入检查之前。重复查询场景已观察到三条请求、两条准许和一条拒绝。

`[tool-call]` 表示函数收到请求，包含后来被拒绝的调用；`[tool-admitted]` 表示获准尝试，不表示查询成功或文件已经读到。本课需要观察两者的区别。

在 Program.cs 创建 Agent 前，准备本次运行的对象并注册实例方法：

```csharp
var toolBudget = new ToolCallBudget(limit: 2);
var serviceTools = new ServiceTools(toolBudget);
// ChatOptions 中原有注册改为：
// Tools = [AIFunctionFactory.Create(serviceTools.GetServiceHealth)]
```

目前 CLI 一次进程只执行一次 Agent 运行，因此上述生命周期满足练习要求。如果以后复用 Agent 处理多次运行，不能直接沿用这个对象生命周期，必须重新设计每次运行的状态隔离。

## 任务三 用确定性调用验证

新增 `budget-check` 模式，在 Program.cs 原有模式校验及客户端创建之前处理。该模式不创建模型客户端，通过 AIFunctionFactory.Create 包装同一个工具实例，再使用 AIFunction.InvokeAsync 顺序提交参数；使用真实模型时也注册这个实例方法。

每组实验创建新的预算和工具对象，组内不能重建。添加断言并打印 PASS/FAIL；所有检查通过返回退出码 0，否则非 0。

早期实现直接调用函数三次，丢弃返回值后 return 0，不能自动发现“第三次错误放行”的回归。本人现已接入下列重复 ID 场景检查并通过验收：Program.cs 顶部增加 `using System.Text.Json;`，budget-check 分支保存返回值并进行断言。

```csharp
if (mode == "budget-check")
{
    var budget = new ToolCallBudget(2);
    var checkingTools = new ServiceTools(budget);
    var tool = AIFunctionFactory.Create(checkingTools.GetServiceHealth);
    var results = new List<JsonElement>();

    for (int i = 0; i < 3; i++)
    {
        object? raw = await tool.InvokeAsync(
            new AIFunctionArguments { ["serviceId"] = "order-api" });
        if (raw is not JsonElement result)
        {
            Console.Error.WriteLine("FAIL: 工具结果类型不符合预期。");
            return 1;
        }
        results.Add(result);
    }

    JsonElement third = results[2];
    bool passed = results[0].GetProperty("ok").GetBoolean()
        && results[1].GetProperty("ok").GetBoolean()
        && !third.GetProperty("ok").GetBoolean()
        && third.TryGetProperty("errorCode", out var code)
        && code.GetString() == "TOOL_BUDGET_EXCEEDED"
        && third.TryGetProperty("serviceId", out var id)
        && id.GetString() == "order-api"
        && third.TryGetProperty("maxToolExecutions", out var max)
        && max.GetInt32() == 2
        && budget.Used == 2;

    Console.WriteLine(passed ? "PASS: 重复查询预算检查" : "FAIL: 重复查询预算检查");
    return passed ? 0 : 1;
}
```

第三次返回业务拒绝是本用例的预期结果，因此测试应通过；第三次被放行才应使测试失败。这个片段保留最初单场景的检查方式，当前应用已按下表参数化。若以后将检查器扩展为通用工具，应捕获并报告结果格式异常，让失败信息也能明确定位用例。

| 实验 | 依次传入 serviceId | 预期业务结果 | 最终 Used |
| --- | --- | --- | --- |
| 重复调用 | order-api、order-api、order-api | 成功、成功、TOOL_BUDGET_EXCEEDED | 2 |
| 更换参数 | order-api、inventory-api、order-api | 成功、成功、TOOL_BUDGET_EXCEEDED | 2 |
| 失败也扣额度 | billing-api、order-api、inventory-api | UNKNOWN_SERVICE、成功、TOOL_BUDGET_EXCEEDED | 2 |

每组应有 3 条 `[tool-call]`、2 条 `[tool-admitted]`、1 条 `[tool-rejected]`。当前三组连续执行，每组创建自己的预算并从新额度开始，已覆盖这些用例之间不继承已消耗额度的基本情形，不再要求额外的新旧对象隔离用例。这不代表未来共享宿主中的任务隔离已经验证。

最后直接并发调用同一个预算对象的 TryConsume 20 次，断言成功次数为 2、Used 为 2。这个检查只验证计数器，不代表已经测试 MAF 的并行工具调度。

```bash
dotnet build apps/FirstAgent/FirstAgent.csproj --no-restore
dotnet run --project apps/FirstAgent --no-build --no-restore -- budget-check
```

当前应用已复用检查逻辑覆盖上述场景：服务 ID 序列和预期错误码作为参数，没有把所有用例都写成“前两次成功”，结果断言也不固定 order-api。每组独立创建预算，组内共享；逐项输出结果后汇总，全部通过返回 0，任一失败返回非 0。

未知服务场景中第一项应断言 ok=false 且 errorCode=UNKNOWN_SERVICE，第二项成功，第三项超限；这组用于证明失败也扣额度。

### 多场景复用时参数化预期

此前 BudgetChecking 只参数化服务 ID，固定断言导致失败扣额场景被误判。本人现已增加用例名和预期错误码数组，并正确比较各位置结果，三个序列均通过。方法参数形状为 `BudgetChecking(string caseName, string[] serviceIds, string?[] expectedErrors)`。本阶段每组三次调用，输入与预期数组必须等长。

约定 null 表示预期成功，字符串表示预期失败且必须匹配该错误码。为三个序列分别传入：

| 用例 | expectedErrors |
| --- | --- |
| 重复 ID | `[null, null, "TOOL_BUDGET_EXCEEDED"]` |
| 更换 ID | `[null, null, "TOOL_BUDGET_EXCEEDED"]` |
| 失败也扣额度 | `["UNKNOWN_SERVICE", null, "TOOL_BUDGET_EXCEEDED"]` |

收集结果后，按预期逐项判断。本人当前实现与以下比较逻辑等价：

```csharp
bool passed = budget.Used == 2;
for (int i = 0; i < results.Count; i++)
{
    string? expectedError = expectedErrors[i];
    bool actualOk = results[i].GetProperty("ok").GetBoolean();
    bool itemPassed = expectedError is null
        ? actualOk
        : !actualOk
            && results[i].TryGetProperty("errorCode", out var error)
            && error.GetString() == expectedError;
    passed &= itemPassed;
}
```

继续保留第三次结果的 maxToolExecutions 断言，将其 serviceId 与 `serviceIds[2]` 比较；输出中使用 caseName 区分用例。不要为了消除 FAIL 而直接接受所有业务错误，只有匹配该位置预期的错误才算通过。

### 本阶段验收与隔离范围

三个序列与第三次返回字段断言均已通过。助手补齐的并发准入检查通过 `Parallel.For` 在同一个新预算上执行 20 次 TryConsume，分别保存 bool 返回值，等待全部完成后断言只有 2 次返回 true 且 Used=2。没有使用未同步的共享 `success++`，避免引入检查器自身的竞争。

本阶段取消单独的新旧预算隔离测试。当前 Used 是实例状态，每次 CLI 运行、每组检查均新建预算；三组检查已经提供基础覆盖。额外手工构造两个预算对象，对当前学习阶段的增量价值低。

隔离的关键是持有状态的 ToolCallBudget 实例和它的生命周期，不只是 ServiceTools 是否 new。例如两个 ServiceTools 可以接收同一个预算，因而共享额度；这也可用于同一任务下多个工具共用限额。当前代码实际同时创建新的 ServiceTools 与 ToolCallBudget，因此没有这类跨组共享。

以后若引入 Agent 复用、依赖注入或多任务宿主，再通过真实运行入口验证不同任务的状态隔离，比单纯手工 new 两个对象更有价值。T04 中“新运行不继承旧额度”的行为目标仍保留，本次只取消重复的独立用例。

三个序列加并发检查，共四项，各自输出 PASS/FAIL，并汇总退出码。2026-11-12 助手从当前源码构建并运行，结果为 4/4、退出 0；CI 已加入同一入口。本轮未调用真实模型。此前临时项目的结果与本轮应用内检查分开记录；助手完成并发检查不等于本人已独立实现。

## 范围与下一步

本课只保证工具入口不再准许第三次执行。模型仍可能继续请求工具并收到拒绝，所以工具执行限额不等于模型请求轮数上限，也不等于任务已经终止。现有 60 秒取消不能替代轮数计数。

后续再验证模型轮数上限、预算耗尽时任务状态如何向宿主传播、是否允许一次最终总结，以及工具超时与取消。当前的 `ClientRetryPolicy(0)` 也不控制 Magpie 内部是否重试。

## 提交与复述

提交编译与 budget-check 输出，代码由助手在工作区核对。用两句话解释：为什么计数不能放在每次工具调用的局部变量里？为什么两次工具额度不能直接等价为两次模型请求？
