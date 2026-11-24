# MAF Harness 阅读地图

用途：H01–H10 每课对照阅读的入口。只列入口文件、带着读的问题和本地环境的限制，不表示已经完成分析。

## 版本约定

- 行数与行号对应标签 `dotnet-1.27.0`（应用锁定的 MAF 版本），用 `git -C agent-framework show dotnet-1.27.0:<路径>` 读取。
- 本地工作区是 `e2f52bc5a`。两者之间 Harness 相关目录有 20 个文件不同，集中在 `ChatClientAgent.cs`、`PerServiceCallChatHistoryPersistingChatClient.cs`、`AgentModeProvider.cs`、`FileMemoryProvider.cs` 和 `AgentHooksFunctionMiddleware.cs`；下表标 † 的文件请按标签读。
- 在线对照：把路径接在 `https://github.com/microsoft/agent-framework/blob/dotnet-1.27.0/` 之后。

## HarnessAgent 实际装配了什么

入口是 `dotnet/src/Microsoft.Agents.AI.Harness/HarnessAgent.cs`（379 行）的 `BuildInnerAgent`（L198–300）和 `BuildAgent`（L167–196）。以下是助手读源码的结论，顺序依据源码注释“先注册的装饰器在外层”，尚未用实验验证。

| 层 | 由外到内 |
| --- | --- |
| Agent 装饰器 | `LoopAgent`（有评估器时才有）→ `ToolApprovalAgent` → `OpenTelemetryAgent` → `ChatClientAgent` |
| 聊天客户端管线 | 审批响应绑定 → 免审批调用直通 → `FunctionInvokingChatClient` → `MessageInjectingChatClient` → 逐次调用历史持久化 → 压缩（`CompactionProvider`，启用时）→ OpenTelemetry → 模型客户端 |
| 上下文 Provider | `TodoProvider`、`AgentModeProvider`、`FileMemoryProvider`、`AgentSkillsProvider` 默认开启；`FileAccessProvider`、`BackgroundAgentsProvider` 需要显式配置 |
| 历史 | 默认 `InMemoryChatHistoryProvider`；启用压缩时把压缩策略挂为它的 `ChatReducer` |
| 指令 | harness 指令在前、应用指令在后，用空行拼接 |

类注释里“聊天客户端管线（由内到外）”的列表，方向与 `BuildInnerAgent` 的注册顺序相反。读的时候以代码为准；H04 用一个探针装饰器验证实际顺序。

## 本地环境的限制

- 官方 Harness 示例（抽查 9 个 README）都以 `FOUNDRY_PROJECT_ENDPOINT` 为前提，在本地 Magpie 网关上不能直接运行。把它们当阅读材料，客户端创建换成 `AsIChatClientWithStoredOutputDisabled`。
- `HarnessAgent` 默认加入 `HostedWebSearchTool`（Foundry 托管搜索），本地需要 `DisableWebSearch = true`。它默认从当前工作目录发现 Skills，并把文件记忆放在当前目录的 `agent-file-memory` 下。
- `Microsoft.Agents.AI.Harness` 包不在本机缓存，安装需要联网。`HarnessAgentOptions` 里压缩、外层循环、文件存储和后台子代理相关属性带 `[Experimental]`。
- 其余类型都在已安装的 `Microsoft.Agents.AI` 1.27.0 里。

## 逐课入口

| 课 | 入口文件（`dotnet/src/` 下，括号内为行数） | 带着读的问题 | 示例（`dotnet/samples/` 下） |
| --- | --- | --- | --- |
| H01 | `Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs`†（1161）的 `RunCoreAsync`，L221；循环本体在外部仓库的 [FunctionInvokingChatClient](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs) | Agent 怎样把消息、指令和工具交给 `IChatClient`？循环里它多处理了什么？ | `01-get-started/02_add_tools` |
| H02 | 外部 `AIFunctionFactory` 与循环的错误处理；`docs/decisions/0002-agent-tools.md` | 未知工具、非法参数、工具抛异常分别变成什么？ | `02-agents/Agents/Agent_Step20_DynamicFunctionTools` |
| H03 | `ChatClientAgent.cs`† 的 `RunCoreStreamingAsync`，L311；`Microsoft.Agents.AI.Abstractions/AgentResponseUpdate.cs` | 流式更新怎样聚合成最终响应？取消时保存什么？ | 无对应示例 |
| H04 | `Microsoft.Agents.AI.Abstractions/AIContextProvider.cs`（511）、`Microsoft.Agents.AI/AIContextProviderDecorators/AIContextProviderChatClient.cs`、`Microsoft.Agents.AI/ChatClient/MessageInjectingChatClient.cs`（391） | Provider 注入的消息和工具落在请求的什么位置？能否在循环中途改？ | `02-agents/Agents/Agent_Step17_AdditionalAIContext`、`Agent_Step11_Middleware` |
| H05 | `Microsoft.Agents.AI/Compaction/`：`CompactionProvider.cs`（226）、`CompactionStrategy.cs`（164）、`CompactionTriggers.cs`（134）、`ContextWindowCompactionStrategy.cs`（148）、`ToolResultCompactionStrategy.cs`（255）、`SummarizationCompactionStrategy.cs`（224） | 什么时候触发？压缩后怎样保持调用与结果成对？请求级压缩和历史级裁剪有何不同？ | `02-agents/Agents/Agent_Step13_ChatReduction`、`Agent_Step18_CompactionPipeline` |
| H06 | `Microsoft.Agents.AI/ChatClient/PerServiceCallChatHistoryPersistingChatClient.cs`†（360）、`Microsoft.Agents.AI.Abstractions/ChatHistoryProvider.cs`（478）、`AgentSession.cs`（119）；`docs/decisions/0022-chat-history-persistence-consistency.md` | 每次模型调用后落盘什么？崩溃后从哪里恢复？ | `02-agents/Agents/Agent_Step19_InFunctionLoopCheckpointing` |
| H07 | `Microsoft.Agents.AI/Harness/`：`Todo/TodoProvider.cs`（388）、`AgentMode/AgentModeProvider.cs`†（443）、`FileMemory/FileMemoryProvider.cs`†（568）、`FileStore/AgentFileStore.cs`（342） | 状态存在会话的哪里？怎样同时提供工具和指令？ | `02-agents/Agents/Agent_Step22_AgentMode`、`Agent_Step23_TodoList`、`02-agents/Harness/Harness_Step01_Research` |
| H08 | `Microsoft.Agents.AI/Harness/ToolApproval/ToolApprovalAgent.cs`（1198）、`Microsoft.Agents.AI/ChatClient/ApprovalResponseBindingChatClient.cs`、`Microsoft.Agents.AI.AgentHooks/`；`docs/decisions/0006-userapproval.md`、`0040-dotnet-agent-hooks-enforcement.md`、`0049-dotnet-unanswered-tool-approvals.md` | 审批绑定到什么？未应答的请求怎样处理？ | `02-agents/Agents/Agent_Step01_UsingFunctionToolsWithApprovals` |
| H09 | `Microsoft.Agents.AI/Harness/Loop/LoopAgent.cs`（541）、`LoopEvaluator.cs`（41）、`AIJudgeLoopEvaluator.cs`（217）；`Microsoft.Agents.AI/Harness/BackgroundAgents/BackgroundAgentsProvider.cs`（914） | 外层循环与工具循环的终止条件有何不同？子代理的结果怎样回到主对话？ | `02-agents/Harness/Harness_Step05_Loop`、`Harness_Step02_Research_WithBackgroundAgents` |
| H10 | `Microsoft.Agents.AI.Harness/HarnessAgent.cs`（379）、`Microsoft.Agents.AI/OpenTelemetryAgent.cs`；`docs/decisions/0003-agent-opentelemetry-instrumentation.md` | 默认开启的能力里哪些本地用不了？`BuildAgent` 的装配顺序对你的 MiniHarness 意味着什么？ | `02-agents/Harness/BuildYourOwnClaw/Claw_Step04_ProductionReady` |

## 相关设计文档

`docs/decisions/` 下还有 0015（agent-run-context）、0019（上下文压缩策略，Python 侧的设计）、0024（提示注入防护）、0030（Agent Skills）。这些是设计材料，可能包含历史命名或尚未覆盖的实现，结论要与代码和测试对照。
