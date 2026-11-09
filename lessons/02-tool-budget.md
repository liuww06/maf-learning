# 第 2 课 工具执行预算

前置结果：[函数工具练习](02-function-tools.md) 的第二轮两个样本满足当前回答要求。下一步实现一个硬约束：单次 Agent 运行最多准许两次服务快照查询，不论模型更换服务 ID 还是重复提出调用。

本课由你实现应用代码。下面的 ToolCallBudget 是我们要新增的业务类型，不是 MAF 内置 API。当前项目尚未实现这项预算。

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

提示：用同一把 `lock` 保护“检查与增加”整个操作；Used 的读取也遵守同步约定。不要先在锁外判断，再单独增加。计数器不能放在工具方法的局部变量中，也不能作为整个进程共享的 static 可变状态。

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

| 实验 | 依次传入 serviceId | 预期业务结果 | 最终 Used |
| --- | --- | --- | --- |
| 重复调用 | order-api、order-api、order-api | 成功、成功、TOOL_BUDGET_EXCEEDED | 2 |
| 更换参数 | order-api、inventory-api、order-api | 成功、成功、TOOL_BUDGET_EXCEEDED | 2 |
| 失败也扣额度 | billing-api、order-api、inventory-api | UNKNOWN_SERVICE、成功、TOOL_BUDGET_EXCEEDED | 2 |

每组应有 3 条 `[tool-call]`、2 条 `[tool-admitted]`、1 条 `[tool-rejected]`。再新建一次运行的对象，确认第一次查询仍能成功，证明不同运行没有共用额度。

最后直接并发调用同一个预算对象的 TryConsume 20 次，断言成功次数为 2、Used 为 2。这个检查只验证计数器，不代表已经测试 MAF 的并行工具调度。

```bash
dotnet build apps/FirstAgent/FirstAgent.csproj --no-restore
dotnet run --project apps/FirstAgent --no-build --no-restore -- budget-check
```

以上是实现后要执行的命令，当前应用尚不支持 budget-check。本课先提交确定性结果，不需要花费模型额度来诱导第三次调用。

## 范围与下一步

本课只保证工具入口不再准许第三次执行。模型仍可能继续请求工具并收到拒绝，所以工具执行限额不等于模型请求轮数上限，也不等于任务已经终止。现有 60 秒取消不能替代轮数计数。

后续再验证模型轮数上限、预算耗尽时任务状态如何向宿主传播、是否允许一次最终总结，以及工具超时与取消。当前的 `ClientRetryPolicy(0)` 也不控制 Magpie 内部是否重试。

## 提交与复述

提交编译与 budget-check 输出，代码由助手在工作区核对。用两句话解释：为什么计数不能放在每次工具调用的局部变量里？为什么两次工具额度不能直接等价为两次模型请求？
