# 2026年11月13日 轮数配置接入与计数辨析

本人解释：LLM 提出工具调用后，Agent 将工具执行结果再次交给 LLM，模型根据新内容继续推理，必要时产生多次工具调用。这个解释说明已理解多轮交互的基本原因；更精确地说，模型提出调用请求，运行时负责实际执行工具。

本人同时提交 budget-check 四项通过的输出，并将并发准入项描述为“20 次 loop，只有 2 次工具使用”。本轮重点纠正计数范围，尚不能据此记录为已经理解模型轮数的运行证据。

## 当前代码与验证

助手只读核对应用代码：本人已声明 MaxToolRounds=2，并通过 Responses AsAIAgent 的 clientFactory 创建 FunctionInvokingChatClient，将 MaximumIterationsPerRequest 设为该常量。其余工具预算逻辑未改动，任务一的配置接入完成。

- 从当前源码构建 Release，成功，0 警告、0 错误。
- 执行 `dotnet apps/FirstAgent/bin/Release/net10.0/FirstAgent.dll budget-check`，四项 PASS、汇总 4/4，总退出码 0。
- 工具请求日志共 9 条，来自前三组序列；每组准许 2 次、拒绝 1 次。
- 学习资料静态检查通过：5 个 JSON 文件、101 个本地文档链接、13 个唯一评测用例；git diff --check 通过。静态检查与应用运行结果分别记录。
- 本轮没有调用模型或 Magpie，没有运行尚不存在的 loop-check，也没有替本人修改应用代码。

构建沿用本机 `/home/leo/.nuget/packages` 缓存，设置临时 DOTNET_CLI_HOME，执行 `dotnet build apps/apps.slnx --configuration Release --no-restore --tl:off`。没有重新还原依赖或更改包版本。

## 这份输出证明什么

| 位置 | 实际执行 | 可以证明 | 不能证明 |
| --- | --- | --- | --- |
| 前三组序列 | 每组经 AIFunction 包装调用 ServiceTools 3 次 | 组内前两次准许，第三次拒绝；失败也扣额 | 模型请求了几轮 |
| 最后一组 | Parallel.For 调用同一个预算的 TryConsume 20 次 | 2 次 true、18 次 false、Used=2 | 执行了 20 轮模型或 2 次服务工具 |

并发组只检查预算对象，不调用 GetServiceHealth。整个 budget-check 分支在创建模型客户端和 Agent 之前返回，因此本次执行的模型调用次数为 0，新接入的 FunctionInvokingChatClient 配置没有在这条命令中被执行。

本次通过的是“配置可编译，既有工具预算检查未回归”。当前应用中模型循环的实际次数仍待验证；不能用昨天助手临时项目的结果替代本人应用验收。

## 下一步

进入 [任务二：离线循环实验](../lessons/02-model-round-limit.md#任务二-不依赖真实模型的循环实验)。本人先实现 ScriptedChatClient 的普通响应和 loop-check 接入，再补流式入口：保留真实的 ChatClientAgent 与 FunctionInvokingChatClient，仅替换底层模型客户端。

脚本在仍提供函数工具时返回一个 GetServiceHealth 调用，没有函数工具时返回固定最终文字；在实际生成响应的位置计数。既定验收为一次 Agent 运行内模型客户端调用 3 次、工具执行 2 次、函数工具数量依次 1/1/0。失败实验保持原断言，将 MaxToolRounds 改为 1，应 FAIL 并退出非 0，随后恢复配置。

本轮更新课程状态、学习记录与知识卡片，未提交或推送。复述问题：如果删掉并发检查中的 Parallel.For，而运行真实的 Agent，为什么模型仍可能请求多轮？应该在哪个边界计数？

## 脚本客户端实现辅导

用户表示不清楚怎样编写脚本化客户端。核对工作区发现本人已创建 ScriptedChatClient，包含接口成员与 Calls 计数；普通方法返回空 ChatResponse，流式方法返回空序列，GetService 在请求 ToolCallBudget 时新建预算。当前没有 loop-check 分支。

助手补充 [逐步讲解](../lessons/02-scripted-client-walkthrough.md)，明确脚本模拟的是模型响应：有函数工具时返回 Assistant 角色中的 FunctionCallContent，无函数工具时返回固定文字。GetService 返回自身或 null，工具预算仍由 ServiceTools 构造函数接收。流式暂时抛 NotSupportedException，先让普通响应有完整执行证据。

本轮没有替用户修改 ScriptedChatClient.cs 或 Program.cs。提供完整参考实现和普通 loop-check 接入片段，待本人阅读、补入应用并解释。助手示例不记为用户独立完成。

### 助手参考示例验证

临时项目 `/tmp/maf-scripted-guide-j24jtq9h/GuideProbe.csproj` 使用与应用一致的锁文件和本地包缓存，包源清空；仅临时项目关闭 NuGet 审计联网。预算类、ServiceTools 通过 Compile Link 引用实际应用源码，读取复制到临时产物目录的合成快照；脚本客户端使用任务单中的参考代码。

首次运行使用独立入口：轮数配置为 2，断言 Calls=3、Used=2、工具数量序列 1/1/0、最终文字符合脚本，输出 PASS、退出 0；配置改为 1 且保留原断言后，Calls=2、Used=1、工具数量序列 1/0，输出 FAIL、退出 1。失败为预期，说明检查能发现配置变化。

随后在临时目录复制用户当前 Program.cs，加入文档中的 using FirstAgent 和 loop-check 分支，验证示例与现有顶层变量、辅助方法可以共存。编译成功，0 警告、0 错误；执行 `dotnet /tmp/maf-scripted-guide-j24jtq9h/bin/Release/net10.0/GuideProbe.dll loop-check` 得到两条实际工具请求日志，以及 `PASS: Calls=3; Used=2; tools=[1,1,0]; text=脚本最终说明`，退出 0。

所有上述结果均来自助手临时项目，没有发起模型或 Magpie 请求，没有验证本轮尚未实现的流式方法。当前用户应用仍停留在接口骨架状态，不将临时接入结果记为工作区已经具备 loop-check。

### 执行职责复述与当前小步

本人正确回答：脚本返回 FunctionCallContent 后，由 FunctionInvokingChatClient 负责执行工具。当前本地函数场景中，它依据工具名称找到已注册的 AIFunction，经包装调用实际 ServiceTools 方法，再将工具结果交回内层客户端。这是执行职责的口述证据，不代表循环验收已经完成。

助手静态看到本人已在 GetResponseAsync 中构造 Assistant 消息与 GetServiceHealth 的 FunctionCallContent，GetService 已不再创建预算。目前仍无条件返回工具请求，下一小步应根据 options.Tools 中是否存在该函数，补充固定最终文字分支；Calls 应在两种响应共用的入口加一，调用 ID 使用该值，不再单独递增。loop-check 尚未接入，流式仍为占位。本轮只更新记录与进度，未修改应用代码、编译或执行新的实验。

### 普通响应分支完成后的审查

本人反馈已完成。核对代码：已按 options.Tools 中是否包含 GetServiceHealth 选择工具请求或固定最终文字，两个分支分别标记 ToolCalls 与 Stop；调用 ID 已使用 Calls 当前值。

仍漏掉 GetResponseAsync 方法入口的 Calls++。按当前源码，新实例仅使用普通入口时 Calls 会保持 0，工具请求也会重复使用 call-0。这是静态发现，不是本轮已执行模型循环所得的计数。修正只需在两个返回分支之前统一递增一次，确保最终文字响应也计入。

助手重新构建当前应用 Release，成功，0 警告、0 错误；没有运行不存在的 loop-check，没有发起模型或 Magpie 请求，也没有重复既有 budget-check。未替本人修改应用代码。普通响应分支已实现，计数与实际循环仍待验收。

### 计数补齐后进入普通循环接入

本人表示已补上计数。助手静态核对：GetResponseAsync 方法开头已有一次 Calls++，两个响应分支共用该计数，工具调用 ID 使用 Calls 当前值，没有重复递增。

当前类还没有 FunctionToolCounts，Program.cs 也尚未加入 loop-check。任务单第二步补充了与本人当前实现匹配的两行观测代码，并明确检查分支应放在 mode 解析之后、原有模式校验之前。接下来由本人一次接入普通循环检查，编译并验证 Calls=3、Used=2、函数工具数量 1/1/0 及最终文字；参考分支会汇总断言并返回退出码。

本轮没有修改应用代码、重新编译或运行模型循环；仅静态确认已要求的计数修正，更新接入指导与进度。没有把先前助手示例的执行结果记为本人应用已通过。流式与宿主终止状态仍留在后续小步。
