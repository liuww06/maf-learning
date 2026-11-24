# H01 自研 Agent 循环

目标：不借助 `FunctionInvokingChatClient` 和 `ChatClientAgent`，自己写工具循环，让停止原因由循环直接给出。分两次课，每次 60–120 分钟。骨架由助手搭建（2026-11-24），关键实现由你完成。

## 已备好的骨架

- `framework/MiniHarness/`：自研 harness 类库。`RunOutcome` 是你 11-23 定义的契约，从 FirstAgent 移来；`AgentLoop.cs` 里有 `AgentLoopOptions`，`AgentLoop.RunAsync` 目前直接抛 `NotImplementedException`。
- `apps/HarnessLab/`：实验宿主。`check` 跑离线检查，分支留给你填；`run "<问题>"` 用自研循环调用真实模型，已经接好。
- `ServiceTools`、`ToolCallBudget` 和服务快照沿用 FirstAgent 的同一份文件。FirstAgent 其余代码不动，留作对照。

开始前先确认现状：下面两条命令应当构建成功，然后以退出码 1 提示“尚未实现”。

```bash
dotnet build apps/apps.slnx --configuration Release --no-restore
dotnet run --project apps/HarnessLab --configuration Release --no-build --no-restore -- check; echo "退出码=$?"
```

## 第一次课：循环与两项检查

**1. `ScriptedModel`**（新建 `apps/HarnessLab/ScriptedModel.cs`）

按队列依次返回预设响应的 `IChatClient`，不访问网络。每收到一次请求，先检查协议：历史里每个 `FunctionCallContent` 都必须有同 `CallId` 的一条 `FunctionResultContent`，否则抛异常。队列用完还被请求，也抛异常。响应的构造方式参考你写的 [ScriptedChatClient](../apps/FirstAgent/ScriptedChatClient.cs)。

**2. `AgentLoop.RunAsync`**（[AgentLoop.cs](../framework/MiniHarness/AgentLoop.cs)）

- 直接调用 `model.GetResponseAsync(messages, options, cancellationToken)`。`options.Instructions` 和 `options.Tools` 取自 `AgentLoopOptions`。
- 响应里有 `FunctionCallContent`：按 `Name` 找工具并执行，把带原 `CallId` 的 `FunctionResultContent` 回填进消息，然后继续请求。
- 响应里没有工具调用：返回 `Completed`，`Text` 为响应文字。
- 工具交互轮数达到 `MaxToolRounds` 后不再请求：返回 `ToolRoundLimitReached`，`Text` 为空。
- 找不到工具：回填一条说明“未知工具”的错误结果，让模型继续。
- `ModelCalls` 和 `ToolExecutions` 由循环自己计数。

工具执行沿用你已用过的方式：`AIFunction.InvokeAsync(new AIFunctionArguments(...))`，参数来自 `FunctionCallContent.Arguments`。

**3. 两项离线检查**（`Program.cs` 的 `check` 分支）

每项新建脚本、预算和工具，打印 PASS 或 FAIL；全部通过返回 0，否则返回 1。

| 场景 | 脚本队列 | 预期 |
| --- | --- | --- |
| 默认，上限 2 | 工具调用、工具调用、文字 | `ModelCalls`=2，`ToolExecutions`=2，`ToolRoundLimitReached` |
| 提前结束 | 工具调用、文字 | `ModelCalls`=2，`ToolExecutions`=1，`Completed` |

## 第二次课：边界、对照与真实模型

**4. 再加两项检查**

| 场景 | 脚本队列 | 预期 |
| --- | --- | --- |
| 同轮两个调用 | 一条响应里两个工具调用、文字 | `ModelCalls`=2，两个都执行 |
| 未知工具 | 未知工具调用、文字 | 模型收到同 `CallId` 的错误结果，`Completed` |

**5. 失败实验**：故意让回填结果使用新的 `CallId`。预期 `ScriptedModel` 的协议检查抛异常，检查 FAIL；恢复后 PASS。

**6. 对照**：同一种脚本走 FirstAgent 的 `loop-check`，Calls=3。解释多出的那次请求从哪里来。

```bash
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check
```

**7. 上限策略**：第一次课用的是“到上限立即停止”。再实现一种：达到上限后撤下工具声明再请求一次（MAF 的做法），或先告知模型额度用尽再请求。用真实模型比较：

```bash
dotnet run --project apps/HarnessLab --configuration Release --no-build --no-restore -- run "请依次查询 order-api、inventory-api、billing-api 的快照状态，每次只调用一个工具，然后汇总。"
```

记录两种策略下的工具日志、最终文字、停止原因和模型请求数。模型可能不按“每次一个”执行，轮数如实记录，这是 H02 的素材。真实模型调用需要先启动本机 Magpie 网关。

**8. 阅读（30 分钟）**：`ChatClientAgent.RunCoreAsync` 和 [FunctionInvokingChatClient](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs)。列出“它多处理了什么”，留作 H02 的输入。

```bash
git -C agent-framework show dotnet-1.27.0:dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs | sed -n '221,310p'
```

## 复述题

- 一次运行为什么有多次模型请求？循环里哪一步决定停止？
- 两种上限策略各自的代价是什么？

## 记录

按 [课后记录模板](../templates/session.md) 写 30 行以内，更新进度表。之后由助手把 `check` 和新锁文件接入 CI。
