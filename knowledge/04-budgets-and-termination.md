# 预算作用域、计数位置与终止

状态：助手准备的知识卡片。用户已实现工具执行预算，提交普通循环正常与失败实验，并完成流式检查，见 [最新验收记录](../progress/2026-11-17-stream-loop-check.md)；宿主终止状态尚待实现。关联：[模型调用循环上限](../lessons/02-model-round-limit.md)。

个人思考：[为什么 Agent 需要工具总调用预算](../reflections/001-tool-call-budget.md)，保存原始疑问、工程判断草稿与复习自测；该笔记的内化状态独立记录。

## 约束应放在被限制动作之前

工具执行预算在函数读取数据或产生副作用之前检查。模型请求总量限制应在每次派发模型请求之前检查。包在整个 Agent 运行外面只能观察顶层调用，不能天然拦截内部每次模型往返。

“一次运行”“一次模型响应”“一次工具调用”“一个流式更新”“一次 HTTP 重试”是不同计量单位。设限前必须写清单位、作用域、扣减时机以及失败是否计数。

当前 budget-check 中的 20 次 Parallel.For 迭代只调用 TryConsume，得到 2 次准许；它们不是 20 次模型请求，也没有在该项中执行 2 次服务查询。判断测试覆盖范围，应沿调用路径确认实际执行了哪些组件，而不能只看日志里都出现了“预算”或“循环”。

## 当前应用中的不同边界

| 机制 | 约束对象 | 不能据此证明 |
| --- | --- | --- |
| ToolCallBudget=2 | 单次运行内获准的工具执行尝试，业务失败仍扣额 | 模型最多请求两次 |
| FunctionInvokingChatClient 的轮数配置 | 自动工具交互循环，边界按版本验证 | 属性值恰好等于全部底层请求数 |
| 60 秒取消令牌 | 到期发出取消请求 | 所有下游动作立即停止 |
| SDK maxRetries=0 | 当前 SDK 层自动重试 | 网关与上游没有自己的重试 |

当前 Extensions 10.10.0 的离线实验证明：轮数配置 2，在脚本持续请求工具时，产生两轮函数交互和一次移除函数工具后的请求，内层客户端共调用 3 次。这个结论不推广到任意版本或复杂嵌套管线。

## 轮数上限能否替代工具预算

不能将 MaximumIterationsPerRequest 理解为工具函数执行总次数。一次模型响应可以包含多个 FunctionCallContent，甚至反复请求同一个已注册工具。AllowConcurrentInvocation=false 只表示同轮工具调用串行执行，不会把同轮调用数量限制为一个。

以下是解释计数单位的假设场景，不是本轮执行日志。设轮数配置为 2，没有工具执行总量预算，且每轮请求的函数都可以正常执行：

| 模型客户端请求 | 本轮提出的工具调用 | 本轮工具执行次数 |
| --- | --- | --- |
| 第 1 次 | 3 个函数调用请求 | 3 |
| 第 2 次 | 3 个函数调用请求 | 3 |
| 第 3 次 | 已移除本地函数工具，返回最终文字 | 0 |

模型客户端请求总共 3 次，工具却执行了 6 次。当前脚本每轮只返回一个调用，是实验脚本的选择，不是框架保证。

两项策略分别表达“最多多少轮模型往返”和“最多准许多少次工具执行尝试”。若业务只要求限制轮数，不要求工具执行总量，可以省略独立工具预算；若仍要求单次任务最多准许两次工具执行，就必须保留该限制。后续可以将检查移到统一工具执行中间件，不必永久保留这个自定义类的具体形状。

依据：实际安装的 Extensions 10.10.0 包内 AllowConcurrentInvocation 文档，以及 [对应版本的循环源码](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs)。本轮只核对源码与文档，没有执行上述多调用场景。

## 达到轮数上限后模型收到什么

2026-11-16 按已安装 Extensions 10.10.0 的 nuspec 固定提交重新核对：`FunctionInvokingChatClient` 在下一次内层模型请求之前检查轮数，调用 `PrepareOptionsForLastIteration` 移除 `options.Tools` 中的 `AIFunctionDeclaration`，保留其他类型的工具。如果移除后没有工具，连同 `ToolMode` 一并置为 `null`；这不是显式设置 `ChatToolMode.None`。

这个上限处理分支没有追加“预算已耗尽，请停止调用”的消息，也没有生成 `TOOL_BUDGET_EXCEEDED` 工具结果。已有的函数调用及其真实结果仍参与下一次模型交互；模型得到的请求不再声明这些函数工具，并获得一次形成文字答复的机会。模型并未因此被告知配置值或耗尽原因，输出也不保证正确、完整。

以配置 2、前两次响应均请求函数的简单路径为例：

| 内层请求 | 框架的处理 |
| --- | --- |
| 第 1 次，iteration=0 | 提供函数工具；执行返回的调用并记录结果 |
| 第 2 次，iteration=1 | 仍提供函数工具；执行返回的调用并记录结果 |
| 第 3 次，iteration=2 | 请求前移除函数工具；收到响应后退出，不再执行其中的函数调用 |

最后一项有代码兜底：普通入口仅在 `iteration < MaximumIterationsPerRequest` 时收集待执行的函数调用；到上限后，即使响应仍包含 `FunctionCallContent`，也不会派发，不会再为它补一个超限工具结果或发起下一轮。响应消息仍汇总返回给调用方。流式入口也在到达上限后跳过函数执行并退出；输出中可能已有调用内容，宿主不能把“循环退出”视为“必然收到最终文字”。

因此需要分开理解三个动作：修改请求中可用的函数工具、用运行时代码禁止继续执行、向模型说明停止原因。前两项在该轮数限制中已有处理；如果产品要求模型明确解释“额度用完、哪些工作尚未完成”，第三项需要应用另行设计，不能假定框架自动补充。

源码依据：[普通入口的请求前检查](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs#L329-L343)、[响应后的执行条件](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs#L368-L407)、[工具过滤方法](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs#L1055-L1086)、[流式退出条件](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs#L664-L681)。本次为源码核对，没有运行模型或新增循环实验；上述配置 2 的流程不表示当前工作区的常量已由 3 改回 2。

## 工具声明与历史上下文有什么区别

从模型本次推理所获信息的角度，工具声明属于上下文，作用类似系统提示中对可用能力的说明。因此，“从本次上下文中移除函数工具声明”是正确的理解。它不意味着删除过去的调用和结果，也不意味着抹掉历史里所有提及该工具的文字。

2026-11-16 本人明确指出了这一点。助手此前将“上下文”狭义理解成历史 messages，以“不是从历史消息中删除”开头，未正面回应模型输入层面的含义，应纠正这一表述。下面的 API 结构区分用于解释实现位置，不能据此否定工具声明属于模型上下文。

实现上，当前应用在 `ChatOptions.Tools` 中注册 AIFunction，`IChatClient.GetResponseAsync(messages, options)` 分别接收历史消息与请求选项。工具描述具体如何编码到模型输入、是否使用某种系统提示模板，取决于服务端实现，本项目尚未验证。

服务端这一层的公开实例与完整链路见 [从 API 字段到模型输入与推理](05-api-to-model-input.md)：Qwen2.5 模板确实可将 tools 写入 system 段，但不能据此推定当前 GLM 网关采用相同实现。

| 内容 | 在当前客户端抽象中的位置 | Responses 适配后的用途 |
| --- | --- | --- |
| 工具声明：名称、描述、参数 schema | `ChatOptions.Tools` | 请求的工具配置 `tools` |
| 历史调用：曾请求哪个函数、参数是什么 | 消息中的 `FunctionCallContent` | 输入中的函数调用记录 |
| 历史结果：该调用返回了什么 | 消息中的 `FunctionResultContent` | 输入中的函数调用结果 |

构造 Agent 时注册一次工具，是保存了可复用的配置；后续每次向模型发请求，客户端仍要根据该次 options 组装工具配置，不是只把声明写入第一条历史消息后就一直沿用聊天记录。当前 Responses 适配器分别将消息转为 `InputItems`，将 `options.Tools` 转为 `CreateResponseOptions.Tools`。实际线上 JSON、服务端会话恢复及 Magpie 的模型输入转换未在本轮抓包验证，不从 SDK 对象推断其内部提示模板。

因此，轮数耗尽时的“移除”只针对下一次请求的函数声明。`PrepareOptionsForLastIteration` 克隆 options 后替换 Tools，并不遍历 messages 删除历史，也不让模型忘记工具名称。历史调用和结果继续参与收尾请求；如果开发者自己在 Instructions 或普通消息中写过工具说明，这个方法也不会删除那些文字。

示意而非抓包：耗尽前包含“已有消息与结果”和“可用函数声明”，耗尽后的收尾请求保留前者、不再提供这些函数声明。这里描述组成，不代表工具声明被排列在模型输入的末尾。模型记得过去调用过什么，与本次是否声明可调用的函数，是两件事；最后一次仍返回调用时，由运行时执行条件阻止派发。

### 工具声明是否放在最后并以 System 角色发送

在当前 MAF 经 Microsoft.Extensions.AI.OpenAI 适配到 Responses 的路径中，函数声明被转换为 `FunctionTool`，加入请求的工具集合；它不属于一条 `role=system` 消息，也没有消息角色字段。`ChatOptions.Instructions` 被写入请求的 Instructions，历史消息单独转换为输入项。

| 请求部分 | 含义 | 能否由此推断模型中的排列与角色 |
| --- | --- | --- |
| `instructions` | 当前配置的指令 | 指令的服务端处理与函数声明转换分开 |
| `input` | 消息及调用、结果等输入项 | 消息有各自角色；调用与结果有各自类型 |
| `tools` | 可用工具声明 | 函数声明自身没有消息 role；字段位置不表示 token 位置 |

MAF 当前这条适配路径没有把工具说明拼成最后一条系统消息。源码中先设置哪个属性、JSON 中字段写在前面还是后面，都不能用来判断最终模型 token 序列的顺序。工具信息仍属于模型可利用的上下文，但它最终是否由服务端编码到系统段、专用工具段或其他结构，需要核对 Magpie 转换和上游模型服务的实现；当前没有这项证据。

依据为已核对的适配器固定提交：[函数声明转换为 FunctionTool](https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs#L870-L883)、[Instructions 与 Tools 分别处理](https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs#L1009-L1059)。概念示意不作为实际请求抓包或模型输入模板证据。

2026-11-16 核对依据：当前 [应用注册位置](../apps/FirstAgent/Program.cs)；Microsoft.Extensions.AI.OpenAI 10.10.1 的 nuspec 提交 `59e1741f6fb45cf7ae08e2df088aef71537a7bc4` 中，[消息转为输入项](https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs#L119-L125)、[工具声明转换](https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs#L1016-L1059)、[历史函数调用转换](https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIResponsesChatClient.cs#L1581-L1588)。本次只读核对代码和文档，没有运行模型请求。

### 换成 OpenAI 兼容的 Chat Completions 接口

“OpenAI 兼容”描述兼容关系，不是唯一的 API 路径。这里按常见的 `/v1/chat/completions`、且服务支持函数工具调用来解释；当前应用仍使用 Responses，没有切换协议。

| 内容 | Responses 示例结构 | Chat Completions 示例结构 |
| --- | --- | --- |
| 指令 | 顶层 `instructions`，也可有带角色的输入消息 | `messages` 中的 system/developer 消息，按服务和模型支持选择 |
| 对话与历史 | `input` 中的消息和调用、结果项 | `messages` |
| 函数声明 | `tools` 中直接放 `type`、`name`、`parameters` 等 | `tools` 中放 `type: function`，声明字段位于内层 `function` |
| 函数调用请求 | `function_call` 输出项 | assistant 消息的 `tool_calls` |
| 函数执行结果 | `function_call_output` 输入项 | `role: tool` 消息，以 `tool_call_id` 关联调用 |

两者的函数声明都独立于普通消息，并非因为 Chat Completions 有 System 角色，就要把工具声明写进 System 消息。`role: tool` 描述的是工具执行结果，不能与请求里的 `tools` 声明混为一谈。接口格式也不证明服务端如何编排模型的实际输入。

当前安装的适配包 10.10.1 中，OpenAIChatClient 将 `ChatOptions.Instructions` 转为 `SystemChatMessage`，将 AIFunctionDeclaration 单独转为 ChatTool 并加入 Tools。若使用同一个 FunctionInvokingChatClient 包装这条客户端路径，轮数耗尽时过滤的仍是 ChatOptions.Tools 中的函数声明，不是删除历史消息。这里是源码层面的对照，本轮没有在兼容服务上运行 Chat Completions 实验。

2026-11-16 核对：[声明转换](https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs#L136-L146)、[指令转为 System 消息](https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs#L150-L158)、[单独设置 Tools](https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs#L636-L644)，以及 [OpenAI 函数调用文档](https://developers.openai.com/api/docs/guides/function-calling) 的 Chat Completions 示例。

## 为什么实际架构会设置工具总量预算

自主 Agent 的工具使用路径由运行中的模型响应决定，派发多少工作不能总在接收任务时确定。工具总量预算为一次任务设定可执行资源上限；是否需要独立计数，应由业务要求和已有控制决定，而不是给所有系统机械添加相同组件。

| 架构目标 | 工具总量预算提供的作用 | 仍需配合的机制 |
| --- | --- | --- |
| 控制成本 | 限制付费查询、计算任务等动作的累计尝试 | 工具成本不同，应有分类或加权预算，必要时按金额计量 |
| 保护依赖 | 限制一个任务对数据库、搜索服务等派发的累计工作 | 并发上限与速率限制控制瞬时负载；超时约束单次等待 |
| 减少无效探索 | 为反复换参数、换工具却没有进展的尝试设置边界 | 根据结果识别无进展；总次数本身不判断工作是否有效 |
| 保持任务间公平 | 减少单次任务独占共享额度和处理能力的机会 | 用户或租户配额、队列与公平调度 |

这是结合通用资源治理原则给本项目的设计建议。微软的 [AI 资源治理指南](https://learn.microsoft.com/en-us/security/zero-trust/catalog-ai-defense-capabilities/resource-governance-abuse-prevention) 将工具调用、步骤、时间、重试、并发和成本列为互补控制；[Copilot Studio 运行时限制](https://learn.microsoft.com/en-us/microsoft-copilot-studio/agents-experience/runtime-limits) 也分别设置单轮工具次数和时间边界。后者是特定产品规则，不是本项目 MAF 包的默认限制。

实现时可把预算放在任务运行上下文中，由统一工具执行入口在派发前检查并占用。多个工具共享同一任务的预算；多 Agent 场景可分配子预算，但不能让每次新建子 Agent 都绕开顶层任务总量。工具内部的 HTTP 重试或批量查询还需要对应层的控制，一次工具准入不天然等于一次外部请求。

次数只是资源消耗的近似指标：一次轻量查询与一次大规模计算不能总按同样成本对待。对写操作，次数上限也不替代授权、审批或幂等。生产阈值应结合任务类型、正常调用分布、成功率、工具成本和允许的服务负载确定，并观察预算命中后任务质量如何变化。本课的 2 次限额用于展示机制，不是生产推荐值。

预算用尽后，应停止继续派发受限工具，由宿主记录终止原因，保留已取得的证据与未完成部分，按策略返回受限结果、请求扩大授权额度或转人工处理。若允许最后一次模型总结，应事先留出模型预算；不能只返回一个错误码就假定模型循环已经终止。当前项目的业务拒绝结果尚未完整实现这种宿主收尾策略。

对于固定、无循环和动态分支、且所有调用及重试已由代码限定的简单流程，可以复用既有上界而不另建同样作用的计数器。是否引入更多机制，应由实际收益决定；这一取舍与 Anthropic 关于从简单可组合流程开始、为自主执行设置停止条件的 [架构说明](https://www.anthropic.com/research/building-effective-agents) 一致。

## 业务错误与框架异常不能混为一谈

工具正常返回 `{ ok: false, errorCode: "TOOL_BUDGET_EXCEEDED" }`，对框架而言仍然是一个正常返回的工具结果。业务字段只有被运行时代码显式解释后，才会改变调度或终止策略。

FunctionInvokingChatClient 的连续错误限制针对工具调用抛出的异常；它不会因为看见业务 JSON 中的 ok=false 就自动消费该异常计数。不要依赖错误码的文字含义来获得运行时硬约束。

## 循环结束与任务成功分别表达

模型的 Stop、函数工具已移除、运行因取消退出，各自表达不同层面的状态。最后请求可以提供一个总结机会，但总结内容仍需要满足业务验收，不能把任何非空文字都当作任务成功。

2026-11-18 本人已完成 [普通与流式的四组对照](../progress/2026-11-18-finish-reason.md)：默认场景三次请求、声明数 `[1,1,0]`；early 场景两次请求、声明数 `[1,1]`；聚合后的 FinishReason 均为 Stop，公共断言均 PASS。这是当前框架与脚本管线中两个不同结束路径产生同一字段值的运行证据，宿主需要额外的运行事实才能区分。

设计宿主结果时，应分开保存运行终止原因和模型输出。输出可以是已完成答复、受限说明或不完整流式片段；未来本课将定义具体结果契约，目前应用没有完整实现这一层。

[通用请求观测练习](../lessons/02-run-observation.md) 将每次底层请求的观测放进可复用包装层，避免宿主依赖脚本替身内部属性。2026-11-19 本人已实现并修复流式计数时机与取消前置检查，助手构建、两组流式回归及四项边界检查通过，见 [验收记录](../progress/2026-11-19-run-observation.md)。包装层只记录事实，不新增预算或直接推断业务成功；本人已解释循环驱动层并明确跳过包装位置失败实验，该项不记为实测通过。

## 用脚本替身验证框架

脚本化 IChatClient 在模型响应边界返回可预测的消息。FunctionCallContent 表示模拟模型提出调用，实际工具执行仍交给框架。GetService 用于查询客户端对象或它提供的服务，不是创建工具预算或执行业务函数的入口。

脚本应查看实际收到的 options.Tools，按是否仍提供目标工具选择返回调用请求或最终文字。若写死“第三次返回最终答案”，即使框架轮数配置失效，脚本也会自行结束，容易造成检查错误通过。完整普通响应参考见 [逐步讲解](../lessons/02-scripted-client-walkthrough.md)。

面试表达练习：先说明限制的对象与作用域，再说明检查位置、失败反馈和终止策略，最后给出运行计数证据。不要仅说“设置一个最大轮数参数就解决了”。

依据、精确包版本与离线实验见 [学习记录](../progress/2026-11-12-model-round-limit.md)；未进行本轮真实模型评测。
