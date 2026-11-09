# MAF 源码阅读地图

基线：`1c3264b065ef4b2b9b3614b19394bdd49d98334f`，核对日期 2026-11-06。下面是阅读顺序和已定位入口，不表示已经完成全部实现分析。源码更新后重新核对路径和行为。

L01 新增应用使用 MAF 1.27.0，其依赖以 [应用锁文件](../apps/FirstAgent/packages.lock.json) 为准，例如 `Microsoft.Extensions.AI.OpenAI 10.10.1`。上游 HEAD 的集中版本声明为 10.11.0；追踪应用行为时优先查看发布包对应源码，可使用 `git -C agent-framework show dotnet-1.27.0:相对文件路径` 阅读标签内容，无需切换当前工作区。

## 从一条请求向下追踪

当前 `ChatClientAgent` 路径可以先按以下层次理解：

```text
业务入口
  → AIAgent.RunAsync / RunStreamingAsync
  → ChatClientAgent.RunCoreAsync / RunCoreStreamingAsync
  → 会话与上下文准备
  → IChatClient 管线
  → 模型 provider
  → 工具请求与执行反馈（使用工具调用管线时）
  → 会话更新与 AgentResponse / 流式事件
```

这是 `ChatClientAgent` 的阅读路线，不能套用为所有远程 Agent provider 的完整实现。

## 阅读单元

| 顺序 | 本地入口 | 要查清的问题 | 要留下的证据 |
| --- | --- | --- | --- |
| 1 | [首个示例](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/samples/01-get-started/01_hello_agent/Program.cs)、[工具示例](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/samples/01-get-started/02_add_tools/Program.cs) | 创建对象、注册工具、运行分别发生在哪里？ | 标注后的调用过程 |
| 2 | [AIAgent](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs) | 公开契约、session、取消与扩展点如何定义？ | 接口职责和不变量 |
| 3 | [ChatClientAgent](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs)、[管线构建](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientBuilderExtensions.cs) | 输入如何组装？客户端管线怎样构建？异常如何传递？ | 断点观察和一条失败路径 |
| 4 | [AgentSession](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI.Abstractions/AgentSession.cs)、[ChatHistoryProvider](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI.Abstractions/ChatHistoryProvider.cs)、[AIContextProvider](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI.Abstractions/AIContextProvider.cs) | 谁持有状态？历史如何装载和保存？上下文从哪里注入？ | 状态读写时间线 |
| 5 | [AIAgentBuilder](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI/AIAgentBuilder.cs)、[DelegatingAIAgent](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI.Abstractions/DelegatingAIAgent.cs)、[OpenTelemetryAgent](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI/OpenTelemetryAgent.cs) | 中间件的包裹顺序影响什么？ | 两种顺序的对照实验 |
| 6 | [WorkflowBuilder](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI.Workflows/WorkflowBuilder.cs)、[Executor](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI.Workflows/Executor.cs)、[Execution](https://github.com/microsoft/agent-framework/tree/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI.Workflows/Execution/) | 构图、调度、消息路由分别由谁负责？ | 执行图和事件顺序 |
| 7 | [Checkpointing](https://github.com/microsoft/agent-framework/tree/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/src/Microsoft.Agents.AI.Workflows/Checkpointing/)、[人工参与示例](https://github.com/microsoft/agent-framework/tree/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/samples/03-workflows/HumanInTheLoop/) | checkpoint 保存什么？等待外部输入怎样恢复？ | 故障注入和恢复记录 |
| 8 | [核心单元测试](https://github.com/microsoft/agent-framework/tree/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/tests/Microsoft.Agents.AI.UnitTests/)、[工作流单元测试](https://github.com/microsoft/agent-framework/tree/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/tests/Microsoft.Agents.AI.Workflows.UnitTests/) | 框架真正承诺的边界行为是什么？ | 一个现有测试解析和一个自己的反例 |

## 关键依赖边界

`ChatClientAgent.RunCoreAsync` 在当前基线调用 `IChatClient.GetResponseAsync`。`IChatClient`、`AIFunction`、消息类型以及 `FunctionInvokingChatClient` 来自 `Microsoft.Extensions.AI` 相关包。当前源码在 [集中包版本文件](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/Directory.Packages.props) 中引用 `Microsoft.Extensions.AI` 的 `10.11.0` 版本。

阅读工具循环需要继续追到该依赖匹配版本的实现，不能把所有循环逻辑归于 MAF，也不能看到一个类型名就认定已理解调用协议。本轮只核对了依赖声明与调用边界，外部依赖的具体循环实现留到 D01。

## 设计文档配合实现阅读

- [工具设计](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/docs/decisions/0002-agent-tools.md)
- [可观测性](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/docs/decisions/0003-agent-opentelemetry-instrumentation.md)
- [用户审批](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/docs/decisions/0006-userapproval.md)
- [中间件](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/docs/decisions/0007-agent-filtering-middleware.md)
- [历史持久化一致性](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/docs/decisions/0022-chat-history-persistence-consistency.md)

这些是设计材料，可能包含历史命名或尚未覆盖的实现；结论必须与当前代码和测试对照。每次只读当前问题相关的文档。

## 每个源码单元的完成方式

1. 先提出一个可验证的问题，例如“模型失败后历史中会留下什么”。
2. 定位公开入口、关键实现、依赖和对应测试。
3. 画出调用或状态变化，记录关键不变量。
4. 运行最小实验，比较预期和观察。
5. 写出如果自己设计，会保留什么、简化什么，以及代价。

第一阶段应用使用已锁定的发布包时，源码阅读要切到与该包对应的来源或明确记录差异。不要默认当前 `main` 与应用依赖完全一致。
