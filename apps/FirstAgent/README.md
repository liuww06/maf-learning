# 第一个 MAF Agent 连接 Magpie Responses

本课应用使用 .NET 10 和 MAF，通过本地 Magpie 网关发送 OpenAI Responses 请求。它有普通响应和流式响应两种模式，现已注册 `GetServiceHealth`，读取虚构服务快照；没有接入实时生产服务。

## 直接运行

在学习仓库根目录运行当前已经编译好的示例：

```bash
dotnet apps/FirstAgent/bin/Debug/net10.0/FirstAgent.dll stream
dotnet apps/FirstAgent/bin/Debug/net10.0/FirstAgent.dll once
```

每条命令会发起一次 Agent 运行，使用实际模型服务。`stream` 逐步显示文字，`once` 等待响应后一次显示。默认模式是 `stream`。可传入自己的问题：

```bash
dotnet apps/FirstAgent/bin/Debug/net10.0/FirstAgent.dll stream "解释工具调用中模型与运行时的职责，控制在80字内。"
```

首次克隆或修改代码后，重新还原、构建和运行：

```bash
dotnet restore apps/FirstAgent/FirstAgent.csproj --locked-mode
dotnet build apps/FirstAgent/FirstAgent.csproj --no-restore
dotnet run --project apps/FirstAgent --no-build --no-restore -- stream
```

`packages.lock.json` 锁定本次解析的依赖。助手本次验证使用 `/tmp/maf-nuget` 作为包缓存；你在自己的终端执行上述还原命令时可以使用默认缓存。当前二进制可直接运行，不需要再次还原依赖。

## 不调用模型的预算检查

工具入口在读取快照前调用 [ToolCallBudget.TryConsume](ToolCallBudget.cs)。每次 CLI Agent 运行最多准许两次执行尝试；业务失败也扣额度，超限返回 `TOOL_BUDGET_EXCEEDED`。同一运行共享预算，新的运行创建新预算。`Used` 仅供近似观测，准入结果以 `TryConsume` 返回值为准。

```bash
dotnet build apps/apps.slnx --configuration Release --no-restore
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- budget-check
```

该模式不创建模型客户端，也不需要 Magpie：前三项通过 AIFunction 包装调用工具，检查重复 ID、更换 ID 和失败扣额；第四项通过 `Parallel.For` 对同一个预算执行 20 次准入尝试，检查准许数与最终 Used 均为 2。四项全部通过退出 0，否则非 0；CI 也执行此入口。

这验证了当前工具预算行为，不代表验证了 MAF 的并行调度或真实模型行为。工具执行预算也不限制模型请求轮数；模型仍可能继续请求并收到拒绝。模型轮数与任务终止属于下一小节。

## 配置

| 环境变量 | 默认值 | 用途 |
| --- | --- | --- |
| MAGPIE_BASE_URL | `http://127.0.0.1:3425/v1` | SDK 的 base URL，保留 `/v1`，不追加 `/responses` |
| MAGPIE_MODEL | `zcode/GLM-5.3-Flash` | 2026-11-09 核对到的源码默认值；可用环境变量覆盖 |
| MAGPIE_API_KEY | `local-placeholder` | SDK 非空凭据占位值；网关开启认证时改用本地实际凭据 |
| MAGPIE_TIMEOUT_SECONDS | `60` | 1–300 秒的运行取消期限 |

程序只读取进程环境变量，不自动加载 `.env` 文件。它不读取 `OPENAI_API_KEY`。本次本地网关配置下，占位凭据已能完成生成请求；这不代表其他网关部署也采用相同认证方式。

例如切换模型：

```bash
MAGPIE_MODEL='codex/gpt-6.1-terra' dotnet apps/FirstAgent/bin/Debug/net10.0/FirstAgent.dll stream
```

该替换模型只在网关列表中观察到，本课尚未实际调用。不要把模型列表中的能力声明视为所有能力都已验证。

## 阅读代码的顺序

先阅读 [Program.cs](Program.cs) 中标有 1、2、3 的部分：

1. `OpenAIClient`：配置连接地址、凭据和 SDK 重试策略。
2. `GetResponsesClient().AsAIAgent(...)`：选择 Responses 协议，并适配为 MAF Agent。
3. `RunAsync` / `RunStreamingAsync`：分别消费完整响应和流式更新。

L02 新增 [ServiceTools.cs](ServiceTools.cs)：函数读取构建时复制的 JSON 快照，通过 `ChatOptions.Tools` 注册到 Agent；宿主打印 `[tool-call]` 与 `[tool-result]` 供观察。

`Instructions` 定义助手的行为要求，`prompt` 是当前用户任务。用户已把指令改为“回答不超过800字”，这属于模型行为引导，代码尚未实现对应的字数校验；客户端取消期限和 SDK 重试次数由代码配置。

本示例发送 `store=false`，不依赖服务端存储的 response ID 继续会话。这个字段不代表整个网关链路完全不记录请求。SDK 自动重试设为 0，仅控制这个 SDK 层，网关和上游是否内部重试尚未检查。

流式统计中的“首更新”指 MAF 返回第一条更新，“首文字”指第一条含文字的更新；它们不是底层 HTTP 首字节时间。更新次数也不是模型请求次数或输出 token 数。

## 本课练习

先运行一次 `stream`，查看回答、首文字耗时和 token 用量。然后修改 `Instructions`，要求回答包含“可提供的帮助”和“当前限制”两项，重新编译并运行同一问题。保存修改和输出，解释指令如何影响回答，以及什么约束仍需要程序保证。

可重复的无网络失败实验：

```bash
MAGPIE_BASE_URL='not-a-url' dotnet apps/FirstAgent/bin/Debug/net10.0/FirstAgent.dll stream
```

预期在发起请求前退出，退出码为 2。也可以在正常运行中按 Ctrl+C 观察取消；实际中途取消尚未在本轮验证，不能由配置存在推断下游立即停止。

## 版本与已验证范围

直接依赖为 `Microsoft.Agents.AI.OpenAI 1.27.0`；锁文件解析出 `OpenAI 2.14.0`、`Microsoft.Extensions.AI.OpenAI 10.10.1` 和 `System.ClientModel 1.16.0`。OpenAI 2.14.0 将使用到的 Responses API 标记为 `OPENAI001` 实验性接口，代码仅在 Agent 创建位置局部抑制该诊断，保留其余警告检查。

2026-11-06：使用 `codex/gpt-6.1-sol` 构建并验证普通、流式各一次真实调用，获得文本与用量；三种非法配置在调用前被拒绝。结果详见 [首次接入记录](../../progress/2026-11-06-l01.md)。

2026-11-09：本人提交指令修改与实际输出，源码默认模型已改为 `zcode/GLM-5.3-Flash`。助手核对代码、移除直接打印凭据和完整用户输入的调试行，重新编译通过；本轮未再次调用模型，也未确认用户这次运行的有效模型和模式。详见 [练习复盘](../../progress/2026-11-09-l01.md)。

同日后续：本人接入函数工具，并提交 GLM-5.3-Flash 流式查询 order-api 与 billing-api 的两轮日志。首轮未知服务回答误将 errorCode 称为证据编号；修改指令并补齐方法描述后，第二轮两个样本满足当时要求。详见 [L02 验证记录](../../progress/2026-11-09-l02.md)。当时下一项 [工具执行预算](../../lessons/02-tool-budget.md) 尚待本人实现，应用尚不支持 budget-check 模式。

2026-11-10：本人已将执行预算接入 ServiceTools，当前每次 CLI 运行最多准许两次工具执行尝试。助手的独立 AIFunction 包装调用验证了第三次拒绝、失败消耗额度和并发上限；超限结果与日志仍需按任务单补齐，budget-check 尚未实现，也未验证模型轮数上限。详见 [预算审查记录](../../progress/2026-11-10-tool-budget.md)。

同日后续：超限结果字段已修正，新增 budget-check 模式，直接调用同一工具实例三次且不创建模型客户端。当前入口丢弃返回值，调用不抛异常就退出 0，只能作为演示；结果断言、AIFunction 包装调用、请求日志位置和其他场景仍待完善。

2026-11-11：budget-check 已通过 AIFunction 包装调用并断言重复查询结果，正常实现输出 PASS、退出 0；在临时副本注入第三次错误放行后，当前检查器输出 FAIL、退出 1。日志顺序及 3/2/1 计数符合约定。其他预算场景尚待纳入该入口，详见 [最新验收记录](../../progress/2026-11-11-tool-budget.md)。

2026-11-12：本人完成三组参数化检查，并补回 serviceId/maxToolExecutions 断言。助手在收尾时补齐并发准入检查、四项结果汇总和 CI 接入；Release 编译通过，四项均 PASS、退出 0。本次未调用模型或 Magpie。经讨论，取消当前阶段重复的新旧对象隔离用例；未来多任务宿主的隔离另行验证。助手补齐的检查不记为本人独立掌握，详见 [预算阶段验收与收尾](../../progress/2026-11-12-tool-budget.md)。

[OpenAI 流式文档](https://developers.openai.com/api/docs/guides/streaming-responses) 说明 HTTP Responses 流式使用 SSE。本课验证的是客户端到 Magpie 这段接口；没有检查 Magpie 内部采用何种上游传输和重试策略。
