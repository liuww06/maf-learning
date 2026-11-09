# 第 0 课 环境与 Agent 执行模型

本课目标：确认开发基线，并能解释一次“模型提出工具请求，程序执行，再把结果交给模型”的过程。预计 60–90 分钟，不需要模型账户或 API 费用。

## 已确认的环境

2026-11-06 已实际执行 `dotnet --info`、`python3 --version`、源码提交查询和文件检查。

| 项目 | 观察结果 |
| --- | --- |
| 操作系统 | Ubuntu 26.04，linux-x64 |
| .NET SDK | 10.0.403 |
| .NET Runtime | 10.0.14 |
| Python | 3.14.4；本主线不要求用 Python 开发应用 |
| MAF 源码提交 | 1c3264b065ef4b2b9b3614b19394bdd49d98334f |
| 应用项目与依赖 | 尚未创建和还原 |
| 模型连接 | 尚未选择和验证 |

需要重新确认环境时，在学习仓库根目录运行：

```bash
dotnet --info
git -C agent-framework rev-parse HEAD
git -C agent-framework status --short
cat agent-framework/dotnet/global.json
```

已有 SDK 与源码 SDK 选择策略匹配，只说明工具版本条件具备，不能代替 NuGet 还原、编译和运行验证。

## 任务一 基础自评

对以下项目分别标记“能独立完成 / 查资料能完成 / 需要补课”：

- 编写调用 HTTP 接口并反序列化 JSON 的 C# 程序。
- 使用 `async/await`，解释 `CancellationToken` 如何传递。
- 使用 `IAsyncEnumerable<T>` 消费异步事件。
- 在 ASP.NET Core 中注册服务，解释 singleton 与 scoped 的区别。
- 编写一个包含失败分支的单元测试。
- 解释 token、上下文窗口和工具调用各自指什么。

这份自评用于调整课程，不要求先全部熟练。

## 任务二 手工执行一次工具循环

先阅读 [服务目录](../fixtures/knowledge/service-catalog.md) 和 [模拟服务数据](../fixtures/service-snapshot.json)。用户提出：“订单服务现在健康吗？请给出依据。”

在纸上或学习记录中补全：

```text
1. 用户消息进入应用
2. 应用准备指令、消息和可用工具描述
3. 模型返回 GetServiceHealth 的调用请求及参数
4. ______ 验证参数和权限
5. ______ 执行 C# 工具，获得状态与观测时间
6. 工具结果通过 ______ 与调用请求关联，加入消息序列
7. ______ 再次请求模型，生成带依据的回答
8. ______ 决定结束，或在轮数/预算/取消条件触发时终止
```

然后加入一个故障：模型请求了 `GetServiceHealth("billing-api")`，但数据集中没有此服务。说明错误由哪层发现、怎样反馈，以及为什么不能把空结果当作健康。

这只是手工协议练习，不是已经发生的真实模型调用。不同 provider 的具体字段和消息形状会在 L01–L02 核对。

## 任务三 读三个短示例

按顺序阅读：

1. [创建和运行 Agent](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/samples/01-get-started/01_hello_agent/Program.cs)：找出 provider、instructions、普通与流式调用。
2. [添加工具](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/samples/01-get-started/02_add_tools/Program.cs)：找出工具描述、参数描述和工具注册。
3. [多轮对话](https://github.com/microsoft/agent-framework/blob/1c3264b065ef4b2b9b3614b19394bdd49d98334f/dotnet/samples/01-get-started/03_multi_turn/Program.cs)：找出 session 的创建和复用。

暂时只阅读。当前源码的这些示例使用 Microsoft Foundry；运行它们需要对应账户、项目和认证配置。我们将在第 1 课按照你实际可用的 provider 创建独立应用，不把 Foundry 账户作为整个课程的前置条件。

## 任务四 用自己的话回答

1. 模型返回一个工具调用请求时，C# 方法是否已经执行？接下来谁负责执行？
2. 一个任务可能调用模型多次，`RunAsync` 为什么仍可表现为一次应用层调用？
3. 为什么“把历史消息传给模型”和“把任务状态保存到数据库”不是同一件事？
4. 查询状态和创建工单，在哪些执行约束上应该不同？

## 验收与记录

提交基础自评、补全后的执行过程、未知服务的处理方案和四个简短回答。我们按回答找出误解，再进入 L01。

使用 [课后记录模板](../templates/session.md) 保存你的结论。环境确认已完成；本课个人练习和理解验收当前仍待完成。
