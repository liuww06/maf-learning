# 第 2 课 宿主结束原因的观测基础

前置：[FinishReason 对照](../progress/2026-11-18-finish-reason.md) 已验收。2026-11-19 本人接入 ObservedChatClient 并修复计数时机与取消检查；助手构建、两组流式回归及四项边界检查通过。本人解释了循环驱动层，并明确决定跳过已熟悉的包装位置失败实验，本小节收尾；跳过不记为实验已执行，见 [本次记录](../progress/2026-11-19-run-observation.md)。

本小步只完成一个能力：在可复用的客户端包装层记录每次模型请求。随后宿主才能依据运行时事实构造结束原因。此前 Calls 和 FunctionToolCounts 属于 ScriptedChatClient；本人现已将宿主读取的数据来源改为包装层，使以后换成真实 SDK 客户端时仍可继续观测。

## 实现位置

```text
ChatClientAgent
  → FunctionInvokingChatClient
    → ObservedChatClient（新包装层，记录每次内层请求）
      → ScriptedChatClient（以后可替换为真实客户端）
```

新建 `apps/FirstAgent/ObservedChatClient.cs`，继承 DelegatingChatClient。它是 IChatClient 的转发基类，可以保留服务发现、释放资源等默认转发行为，只重写本步需要的两个响应方法。

构造函数接收 IChatClient，并传给 `base(innerClient)`。暴露 Calls 和 FunctionToolCounts，构造时均为空或零；每次 Agent 运行创建一个实例。当前串行循环无需增加并发共享、对象池或额外隔离测试。

接入时使用这个顺序：

```csharp
var script = new ScriptedChatClient(earlyMode == "early");
var observed = new ObservedChatClient(script);
using var loop = new FunctionInvokingChatClient(observed)
{
    MaximumIterationsPerRequest = MaxToolRounds
};
```

其余 Agent 和工具注册保持现有逻辑。让两个检查入口的宿主日志与断言读取 observed.Calls、observed.FunctionToolCounts；不要让包装层读取 ScriptedChatClient 的属性，包装层只接收 IChatClient。脚本原有计数及第六次调用的防跑飞保护可以暂时保留，它们不再是宿主观测的数据来源。

## 两个方法的职责

把“Calls 加一、记录本次 options.Tools 中 AIFunctionDeclaration 数量”提取为共用记录方法。只保存计数值，不保存可变的 options 引用或完整消息内容。

- 普通方法：检查取消令牌，记录一次，然后把原 messages、options、cancellationToken 交给 InnerClient.GetResponseAsync，原样返回响应。
- 流式方法：使用带 EnumeratorCancellation 的异步迭代器。开始枚举时检查取消令牌并记录一次，再通过 await foreach 枚举 InnerClient.GetStreamingResponseAsync，将每条更新直接 yield return。必须传递取消令牌；不要逐条更新增加 Calls。

流式包装必须调用内层的流式方法。脚本里“普通响应转更新”的写法用于模拟模型；放进通用包装层会改变真实客户端的流式行为，因此这里直接转发更新。

记录发生在调用内层之前，表示已开始派发的一次 IChatClient 请求尝试；后续失败也保留这次记录。它不代表成功响应数或实际 HTTP 请求数。取消与失败的完整宿主状态在后续处理，本步不要吞掉异常或伪造响应。

首次实现直接在普通方法体里计数并返回内层流式序列，能通过立即完整枚举的用例，但只取得序列而不枚举时也会计数。本人现已改为异步迭代器，计数随开始枚举发生；助手边界检查通过。普通与流式入口均先调用 ThrowIfCancellationRequested；随后发生的取消或内层失败仍可计为一次尝试，不要求取消和派发完全原子化。

## 验收

实现后编译 Release，再运行现有四组检查。保留计数、文字和 FinishReason 的固定断言，只将计数数据源切换到 observed。

| 场景（普通、流式各一组） | observed.Calls | 函数声明数量 | Used | FinishReason |
| --- | --- | --- | --- | --- |
| 默认 | 3 | `[1,1,0]` | 2 | Stop |
| early | 2 | `[1,1]` | 1 | Stop |

四组正常路径已有本人 PASS 输出，修复后的两组流式回归已有助手 PASS/退出 0 证据。本人决定不再重复包装位置失败实验，本小节结束。下面保留实验方法供以后查阅，不作为待完成项。

## 一个失败实验

2026-11-19 本人明确要求跳过本项，原因是已熟悉该调用过程；助手遵从，不将其标为通过，也不要求再次确认。以下为保留的实验说明。

只在普通默认用例中临时把顺序改成 `Agent → ObservedChatClient → FunctionInvokingChatClient → ScriptedChatClient`，仍只保留一层函数循环。保持 observed.Calls=3 的断言不变。

把检查分支内这几个对象的构造改为以下顺序，预算与工具配置继续保留：

```csharp
var script = new ScriptedChatClient(earlyMode == "early");
using var loop = new FunctionInvokingChatClient(script)
{
    MaximumIterationsPerRequest = MaxToolRounds
};
var observed = new ObservedChatClient(loop);
```

同时把 checkingAgent 构造函数的第一个参数从 loop 改成 observed，其余选项保留。重新编译后只运行普通默认 loop-check，紧接着记录退出码。实验完成后恢复本页开头的正确构造顺序及 checkingAgent 的 loop 参数，重新编译并运行同一检查。

预期外层 observed 只看到一次调用，因此 FAIL；内部工具循环仍可执行两次工具。恢复正确顺序并重新构建，普通默认用例应恢复 PASS。这个实验检验中间件放置位置，不改变预算值。

复述：为什么同一个计数器放到循环内外会得到 3 和 1？换成真实客户端时，为什么不能在观测包装层把流式改为普通响应再转换？

## 与宿主结果的关系

下一步再用观测到的请求记录、已核实的框架边界以及异常或取消信号构造宿主结果，区分正常结束、达到限制后收尾和失败。场景参数 early 只控制测试替身，不得作为实际结束原因的来源；Used 达到额度也不等于发生过拒绝。

本步的包装层只记录事实，不新增硬限制。当前版本特定管线里的 N+1 收尾行为也不能直接变成所有框架的通用终止判据。

## API 依据

准备时已核对本机 Microsoft.Extensions.AI.Abstractions 10.10.1 的 XML 文档：DelegatingChatClient、InnerClient、普通与流式响应方法均存在。官方 [DelegatingChatClient 文档](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.delegatingchatclient?view=net-10.0-pp) 对照其转发职责。后续本人实现、本人运行和助手修复后执行验证分别记录，见页首链接。
