# 学习路线：自研 Agent Harness

2026-11-24 重排。原路线把 harness 列为第二阶段选修，与“深入 Agent Harness 开发”的目标不符，结论见 [评审记录](../progress/2026-11-24-curriculum-review.md)。旧路线在提交 fc7a508 的 `docs/roadmap.md`。

## 主线

自研实现放在 `framework/MiniHarness`，实验宿主放在 `apps/HarnessLab`。MAF 的 Harness 实现只作对照阅读，`apps/FirstAgent` 保持现状，作为“框架做法”的参照。每课固定四步：

1. **自己写最小实现。** 关键代码由本人完成。
2. **脚本模型离线检查。** 固定断言，用退出码判定。
3. **同一场景过一次真实模型。** 记录模型标识、提示版本和输出。
4. **读 MAF 对应实现。** 每课只读一两个文件，列出“它多处理了什么”。

失败实验和复述问题只安排在标 ★ 的课上。入口文件见 [Harness 阅读地图](../source-notes/01-harness-reading-map.md)。

| 课 | 能力 | 你写的 | MAF 对照 | 课次 |
| --- | --- | --- | --- | --- |
| [H01](../lessons/H01-agent-loop.md) ★ | Agent 循环：消息、工具调用、callId 配对、停止原因 | `AgentLoop.RunAsync` | `ChatClientAgent.RunCoreAsync`；外部 `FunctionInvokingChatClient` | 2 |
| H02 ★ | 工具系统：注册表、schema、参数绑定、未知工具、同轮多调用、超时 | `ToolRegistry` | 外部 `AIFunctionFactory` 与循环的错误处理；ADR 0002 | 2 |
| H03 | 流式与事件：增量聚合、中途取消、回归测试项目 | 流式循环、`RunEvent` | `ChatClientAgent.RunCoreStreamingAsync`；外部循环的流式入口 | 2 |
| H04 | 上下文组装：instructions、历史、工具声明、Provider 注入 | `IContextProvider` | `AIContextProvider`、`AIContextProviderChatClient`、`MessageInjectingChatClient` | 1–2 |
| H05 ★ | 上下文窗口：触发条件、截断、滑动窗口、工具结果压缩、摘要 | 1–2 个压缩策略 | `Compaction/` 下的 Provider、触发器与各策略 | 2–3 |
| H06 ★ | 会话与持久化：历史 Provider、逐次调用落盘、崩溃后续跑 | 历史 Provider 与恢复 | `PerServiceCallChatHistoryPersistingChatClient`、`ChatHistoryProvider`、`AgentSession`；ADR 0022 | 2 |
| H07 | Harness 级 Provider：Todo、模式、文件记忆（选做两个） | 1–2 个 Provider | `Harness/` 下的 `TodoProvider`、`AgentModeProvider`、`FileMemoryProvider` | 2 |
| H08 | 权限与审批：需审批的工具、审批与调用绑定、Hooks | 审批中间件 | `ToolApprovalAgent`、审批绑定客户端、`AgentHooks`；ADR 0006、0040 | 2 |
| H09 | 外层循环与委派：停止评估器、后台子代理 | `LoopAgent` 的最小版 | `LoopAgent` 与各 `LoopEvaluator`、`BackgroundAgentsProvider` | 2 |
| H10 | 收口：可观测、与 MAF 整体对照、ADR、面试表达 | 对照报告与 ADR | `HarnessAgent.BuildAgent`、`OpenTelemetryAgent`；ADR 0003 | 2 |

## 节奏

每周 5 小时以内，约两次课，每次 60–120 分钟。主线约 20 次课，粗估 10–13 周。

三个里程碑各配一次两分钟口述：

- H03 之后：自己写 Agent loop 最少需要什么，模型一直调用工具时如何终止。
- H06 之后：如何控制长会话成本，工具循环中途崩溃后怎样续跑。
- H10 之后：审批到底授权了什么，单次运行、外层循环和子代理委派各自的取舍。

## 选修

主线完成后再选：RAG、显式工作流、ASP.NET Core 托管、MCP、Shell 与沙箱。[项目设计](project-brief.md) 的工具契约和 `fixtures/` 的虚构数据保留为测试场景。

## 完成标准

- MiniHarness 能在本地网关的真实模型上完成多步工具任务，上下文超限时自动压缩，中断后能恢复，需要审批的工具未经批准不执行。
- 确定性检查覆盖每个能力的正常路径和关键失败路径。
- 至少三份设计决策记录（模板见 [adr](../templates/adr.md)），说明与 MAF 做法的异同和代价。
- 同一场景集上 MiniHarness 与 MAF 的对照报告：行为、调用数、延迟。

## 已完成的基础

L00–L02 的任务单和记录保留：[基线](../lessons/00-baseline.md)、[第一个 Agent](../lessons/01-first-agent.md)、[函数工具](../lessons/02-function-tools.md)、[工具执行预算](../lessons/02-tool-budget.md)、[模型循环上限](../lessons/02-model-round-limit.md)、[请求观测](../lessons/02-run-observation.md)。其中 `ToolCallBudget`、`ServiceTools` 和脚本化模型的做法会直接用到。
