# 第 2 课 模型调用循环的上限

前置：[工具执行预算](02-tool-budget.md) 已验收。当前分支为 `feat/model-round-limit`。

本小节聚焦一个能力：在 MAF 的自动工具循环中设置明确上限，并通过底层客户端调用次数验证边界。关键应用修改由本人完成；助手只准备任务单，并在独立临时项目验证所用 API 与边界行为。

2026-11-13：本人已完成任务一的配置接入，助手复验 Release 编译与既有 budget-check 均通过。当前进入任务二；应用内循环次数尚未验证，见 [配置验收与计数辨析](../progress/2026-11-13-model-round-limit.md)。

2026-11-16：本人已提交普通入口配置 3 的预期 FAIL/退出 1 和恢复为 2 后的 PASS/退出 0，普通循环与该失败实验验收通过；见 [当日记录](../progress/2026-11-16-loop-check.md)。当时流式仍为占位实现。已解释检查器失败退出码，第四次请求的运行时因果仍待本人复述。

2026-11-17：本人补齐流式脚本和检查入口，提交重新构建后普通与流式均 PASS/退出 0 的证据；见 [最新记录](../progress/2026-11-17-stream-loop-check.md)。下一步是本页的提前完成与受限收尾对照。

## 先区分三种次数

| 计数 | 当前例子 | 在哪里观察 |
| --- | --- | --- |
| Agent 运行次数 | 一次 RunAsync 或完整枚举一次 RunStreamingAsync | 宿主入口 |
| 模型客户端调用次数 | 一次运行内可能多次请求模型 | 工具循环内部的底层 IChatClient |
| 获准工具执行次数 | ToolCallBudget 上限为 2，业务失败也扣额 | TryConsume |

模型请求可产生零个、一个或多个工具调用，所以三种次数不能互换。流式更新数也不等于模型调用次数。

MaximumIterationsPerRequest 控制循环轮数；同一轮可有多个工具调用，即使默认串行执行也一样。因此当前“单次任务最多准许两次工具执行”的 ToolCallBudget 不能由该参数等价替代。是否保留独立工具预算取决于业务是否需要总量限制，详见 [轮数与执行总量的区别](../knowledge/04-budgets-and-termination.md#轮数上限能否替代工具预算)。

现有 budget-check 的最后一项使用 Parallel.For 调用 TryConsume 20 次，只有 2 次返回 true；这一项不执行服务工具，也不调用模型。整个 budget-check 在创建 Agent 之前返回，所以它验证的是工具预算对象与工具入口，不能证明新配置的模型循环上限。

工具返回 `ok=false`、`TOOL_BUDGET_EXCEEDED` 是业务结果。当前框架不会根据这个自定义字段自动结束循环；它仍可把结果交给模型。应用已有 60 秒取消和框架默认轮数限制，不能将现状描述为必定无限运行。

## 当前版本的一个边界

锁文件为 MAF 1.27.0、Microsoft.Extensions.AI 10.10.0。后者的 `FunctionInvokingChatClient.MaximumIterationsPerRequest` 默认值为 40。

本课不把这个属性直接当作“全部模型请求次数”的上限。对应版本实现从 iteration=0 开始，在达到设定值后移除函数工具，仍向内层客户端发起一次最后请求，然后结束循环。助手用实际安装包验证：设为 2，脚本每次在仍有工具时提出一个函数调用，普通与流式两种入口均得到：

```text
模型请求 #1：提供函数工具 → 提出调用 → 执行工具
模型请求 #2：提供函数工具 → 提出调用 → 执行工具
模型请求 #3：移除函数工具 → 脚本返回最终文字 → 循环结束
```

因此，在本课这个没有审批、额外重试或其他循环包装的路径中，配置 2 可以有 3 次模型客户端调用，而不是 2 次。模型提前给出最终答复时会更早结束。

这里的最后请求提供了一次总结机会，不保证真实模型一定生成合格总结，也不保证网关内部只有一次 HTTP 请求。官方属性文档描述包含首次请求，与本包实际边界存在差异；以锁定版本源码及运行证据为本课依据，升级后应重验。

## 任务一 配置现有框架循环

先只做这一项，不新增另一个预算计数器。

在 Program.cs 中声明 `const int MaxToolRounds = 2;`，表示本练习允许的工具交互轮数。现有 Responses `AsAIAgent(...)` 调用已有 `model: model`，在后面加入以下命名参数：

```csharp
clientFactory: inner => new FunctionInvokingChatClient(inner)
{
    MaximumIterationsPerRequest = MaxToolRounds
}
```

保留原来的 ChatOptions、工具注册、Responses 配置及 ToolCallBudget。这个参数接收底层 IChatClient 并返回包装后的客户端。MAF 1.27.0 会查找已有 FunctionInvokingChatClient，找到后不会再默认添加一层同类循环。

不要在运行过程中修改这个属性。当前 CLI 每次进程只执行一次 Agent 运行，配置在创建 Agent 时固定即可。

```bash
dotnet build apps/apps.slnx --configuration Release --no-restore
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- budget-check
```

这一小步的验收：编译成功、既有四项预算检查仍通过。它只能证明配置可编译且原有预算检查未回归，尚不能证明模型循环的实际次数。提交修改与输出后，再接任务二。

## 任务二 不依赖真实模型的循环实验

下一小步实现 `loop-check`，在模型客户端创建前进入该分支。使用脚本化 IChatClient 替代网络客户端，同时保留真正的 FunctionInvokingChatClient 和 ChatClientAgent。

实现顺序：先新建 `apps/FirstAgent/ScriptedChatClient.cs`，完成普通响应与 RunAsync 检查，再补流式实现。不要手写 for 循环直接调用工具来冒充 MAF 的模型循环；循环应由被测的 FunctionInvokingChatClient 驱动。

如果还不清楚 IChatClient 各成员怎样实现，先看 [脚本化客户端逐步讲解](02-scripted-client-walkthrough.md)：包含各方法职责、完整普通响应示例和 loop-check 接入片段。参考示例由助手提供；本人现已提交普通与流式入口的应用运行结果，证据见页首记录。

脚本规则如下：

1. 每次实际生成一个完整响应时 Calls 加 1，记录该次 ChatOptions 中的函数工具数量。流式模式按一次响应计数，不能每个更新都加 1。
2. 如果仍提供 GetServiceHealth，返回 assistant 消息中的 FunctionCallContent；名称为 GetServiceHealth，参数为 `serviceId=order-api`，每次使用不同 callId。
3. 如果函数工具已移除，返回固定文字“脚本最终说明”，并标记 Stop。这是模拟响应，不是真实服务状态。
4. 脚本第 6 次被调用时抛出测试专用异常，防止漏配限制时实验继续循环；这只是实验保护，不是被测的运行时预算实现。

普通响应用 `Task<ChatResponse>` 返回；流式可通过 `ChatResponse.ToChatResponseUpdates()` 产生更新。当前小步由流式方法复用 GetResponseAsync 构造一次响应，也可后续抽出共用构造方法；重点是避免重复计数。调用方必须完整枚举流式结果后再验收。

简化管线为：

```text
ChatClientAgent
  → FunctionInvokingChatClient（MaxToolRounds=2）
    → 脚本化 IChatClient（在这里计数）
```

两种入口分别创建本次运行的脚本客户端、ToolCallBudget 和 ServiceTools。这是正常的用例准备，不另加新旧对象隔离检查。

固定验收结果：模型客户端调用 3 次，函数工具数量依次为 1、1、0；服务工具执行 2 次、Used=2；最终文字是脚本返回值。每种入口打印 PASS/FAIL，全部通过退出 0，否则非 0。不要为了让结果通过而把预期值直接设为实测值。

计数应放在工具循环内部。如果只在包住整个循环的外层记一次，就只能看到一次 Agent/客户端调用，无法测量内部模型往返。

## 一个失败实验

2026-11-16 已完成的变式：MaxToolRounds=3、保留固定断言时，普通 loop-check 得到 Calls=4、Used=2、tools=[1,1,1,0]，FAIL、退出 1；第三次执行尝试被工具预算拒绝，之后仍有一次撤下函数工具的请求。本人改回 2、编译后运行得到 PASS、退出 0。该变式已取代下面的改为 1 步骤，不要求重复两个失败实验。

完成 loop-check 后，临时把共享的 MaxToolRounds 从 2 改成 1，保持原本的三次模型调用、两次工具执行断言不变。预期脚本只收到 2 次模型调用，工具只执行 1 次；检查器应 FAIL、退出非 0。随后还原配置。

如果仍然 PASS，检查配置是否真正接入、是否运行旧二进制，以及检查器是否只是打印次数而没有断言。不要在应用中注入无限循环，也不需要付费诱导真实模型反复调用。

## 本小节结束后的下一步

框架循环上限、计数和 FinishReason 对照实验已完成。接着定义宿主的终止结果：正常完成、达到限制后结束、取消、请求失败。`FinishReason=Stop` 只描述模型响应，不独自证明业务任务成功；当前 Program.cs 仍没有专门的模型预算终止状态。下一步先完成 [通用请求观测包装层](02-run-observation.md)，为宿主提供运行时事实。

如果需求改成“整个任务严格最多 N 次底层模型调用，耗尽后也不允许额外总结”，需要在循环内部的模型调用边界实施总准入限制，并让宿主处理专用终止结果。不要直接把这里的属性名或当前 N+1 行为当作所有版本、所有组合管线的通用契约。

复述问题：一次 Agent 运行为什么可能有三次模型请求？工具执行预算为何不能代替轮数限制？为什么计数器放在循环外层会漏计？最后一次模型返回 Stop 是否足以认定任务成功？

## 下一小步 提前完成与受限收尾对照

状态：2026-11-18 本人提交四组 PASS 输出，公共 FinishReason 断言已接入，均打印 finish=stop，见 [验收记录](../progress/2026-11-18-finish-reason.md)。本步已完成，下面保留任务过程；下一步为 [通用请求观测包装层](02-run-observation.md)。

1. 给 ScriptedChatClient 增加可选的提前结束场景，默认保留当前行为。提前结束场景首次仍请求 GetServiceHealth；后续请求中，只要输入 messages 已包含 FunctionResultContent，就返回“脚本正常结束”及 Stop，即使函数工具仍然可用。判断依据是收到工具结果，不要根据 Calls==2 强行停止；公共计数与记录仍在分支之前执行。
2. 在现有两个检查入口接受可选第二参数 `early`，据此构造脚本并选择固定预期；不传参数继续运行原场景。流式入口沿用现有转换逻辑。
3. 在检查日志和断言中加入 response.FinishReason，观察以下差异。默认用例仍保留原来的文字断言，early 使用“脚本正常结束”。

| 场景 | Calls | Used | 函数声明数量序列 | FinishReason 预期 |
| --- | --- | --- | --- | --- |
| 默认：持续请求工具，直到声明被撤下 | 3 | 2 | `[1,1,0]` | Stop |
| early：收到一次工具结果后返回文字 | 2 | 1 | `[1,1]` | Stop |

本步原验收命令如下，四组断言现已通过，无需为重复确认再次执行。后续修改需要重验时，每条运行命令后立即执行 `echo "退出码=$?"`，记录各自的退出码。

```bash
dotnet build apps/apps.slnx --configuration Release --no-restore
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check early
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check-stream
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check-stream early
```

每种入口按场景断言固定值，成功退出 0；失败返回非 0。两种场景的对照说明，仅凭 Stop 无法分辨是否走过轮数耗尽的收尾请求。这里的“正常结束”是脚本控制流标签，不代表真实诊断任务已成功。

本课已有配置 3 的失败实验，本步不重复改变轮数。它也不新增预算计数器或按场景名伪造宿主终止状态；后续宿主需要记录实际运行事实，不能从模型措辞、测试场景名或 Stop 猜测结果。

复述问题：两种路径都返回 Stop，为什么宿主仍需要单独记录是否触达轮数限制？early 场景的第二次请求为什么仍带函数声明？

## 版本依据与验证记录

- [应用锁文件](../apps/FirstAgent/packages.lock.json)：本课实际依赖版本。
- [属性文档](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.functioninvokingchatclient.maximumiterationsperrequest)：辅助对照，不能替代版本实验。
- [Extensions 对应提交的循环实现](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs)：普通入口约 329–407 行，流式入口约 520–668 行，移除函数工具约 1052–1086 行。提交来自本地包 nuspec 的 repository 元数据。
- [MAF 1.27.0 Responses 接入](https://github.com/microsoft/agent-framework/blob/4d1674b78951d6fd127ada532660514637f906e9/dotnet/src/Microsoft.Agents.AI.OpenAI/Extensions/OpenAIResponseClientExtensions.cs) 与 [默认管线构建](https://github.com/microsoft/agent-framework/blob/4d1674b78951d6fd127ada532660514637f906e9/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientExtensions.cs)：clientFactory 的调用位置，以及已有循环客户端的检测。
- [本次准备与独立验证记录](../progress/2026-11-12-model-round-limit.md)、[预算与终止知识卡片](../knowledge/04-budgets-and-termination.md)。
