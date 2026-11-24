# 脚本化客户端：从接口骨架到一次普通响应实验

关联：[模型调用循环上限任务二](02-model-round-limit.md#任务二-不依赖真实模型的循环实验)。最初普通响应参考示例由助手提供并在独立项目验证。2026-11-16 本人提交普通正常与失败实验输出；2026-11-17 补齐流式并提交普通回归、流式检查通过的结果，见 [最新验收记录](../progress/2026-11-17-stream-loop-check.md)。下面保留实现过程；参考材料、运行验收与本人掌握分别记录。

## 它在模拟谁

真实客户端把 messages 和 options 发给模型服务，再将响应转成 ChatResponse。脚本客户端接收同样的参数，用固定规则直接构造 ChatResponse。因此 MAF 仍然按自己的逻辑处理工具调用、执行函数和继续循环，但实验没有网络请求。

本例中，messages 是传入的对话历史，options.Tools 是本次请求提供给模型的工具。脚本不理解问题，也不推理，只执行两个分支：提供 GetServiceHealth 时提出调用请求；函数工具已移除时返回固定文字。

```text
MAF 请求一次模型响应
  → ScriptedChatClient.GetResponseAsync
    → 读取 options.Tools
      → 有 GetServiceHealth：返回 FunctionCallContent
      → 没有：返回 TextContent（可用 ChatMessage 的文字构造函数）
```

返回 FunctionCallContent 的消息角色仍是 Assistant：这是模型提出的请求。随后 MAF 才查找已注册函数、执行 ServiceTools，并把结果放进下一轮消息。脚本客户端不直接调用 ServiceTools。

## 对照你已经写好的骨架

| 成员 | 本例职责 | 最初骨架需调整之处 |
| --- | --- | --- |
| GetResponseAsync | 产生一次完整的模拟模型响应 | 空 ChatResponse 没有工具请求，需要填充消息内容 |
| GetService | 返回当前客户端能提供的对象，例如自身 | 不应在这里 new ToolCallBudget；业务预算由工具实例持有 |
| GetStreamingResponseAsync | 返回响应的流式更新 | 本步先明确不支持，避免空序列被误认为已经实现 |
| Dispose | 释放自己持有的资源 | 当前没有需要释放的资源，可以为空 |

GetService 不是业务函数执行入口。MAF 查找本地工具使用 ChatOptions.Tools 中注册的 AIFunction；这里的 GetService 用于查询客户端对象或它暴露的服务。

## 第一步 补齐普通响应客户端

下面的参考代码对应你已创建的 [ScriptedChatClient.cs](../apps/FirstAgent/ScriptedChatClient.cs)。保留 namespace FirstAgent。每段读懂后再补入自己的文件。

```csharp
using Microsoft.Extensions.AI;

namespace FirstAgent;

// 教学替身：每个实验新建实例，只供一次串行 Agent 运行使用。
public sealed class ScriptedChatClient : IChatClient
{
    public int Calls { get; private set; }
    public List<int> FunctionToolCounts { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        if (Calls >= 6)
            throw new InvalidOperationException("脚本调用达到 6 次，请检查轮数限制是否接入。");

        var functions = options?.Tools?
            .OfType<AIFunctionDeclaration>().ToArray() ?? [];
        FunctionToolCounts.Add(functions.Length);
        bool canCall = functions.Any(f => f.Name == "GetServiceHealth");

        ChatMessage message;
        ChatFinishReason finishReason;
        if (canCall)
        {
            var call = new FunctionCallContent(
                $"script-call-{Calls}",
                "GetServiceHealth",
                new Dictionary<string, object?> { ["serviceId"] = "order-api" });
            message = new ChatMessage(ChatRole.Assistant, [call]);
            finishReason = ChatFinishReason.ToolCalls;
        }
        else
        {
            message = new ChatMessage(ChatRole.Assistant, "脚本最终说明");
            finishReason = ChatFinishReason.Stop;
        }

        return Task.FromResult(new ChatResponse(message)
        {
            ResponseId = $"script-response-{Calls}",
            FinishReason = finishReason
        });
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this : null;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("本步只演示普通响应，流式下一步实现。");

    public void Dispose() { }
}
```

各段的作用：

1. Calls 统计进入普通模型响应入口的次数；实验第六次调用时报错，是漏配循环上限时的保护。
2. OfType<AIFunctionDeclaration>() 筛选本地函数工具声明，再检查 GetServiceHealth 是否可用，并保存数量供断言。
3. FunctionCallContent 的三个参数分别是调用 ID、函数名和参数字典。每次生成不同 ID，便于框架把调用请求和结果对应起来。
4. ChatMessage 的 Assistant 角色表示模拟模型的输出；FinishReason 区分工具调用与文字完成，ResponseId 标识这次模拟响应。
5. Task.FromResult 包装已经构造好的响应。本例无需等待网络，所以 GetResponseAsync 不必写 async/await。
6. GetService 在请求的类型能由当前实例满足时返回 this，其他情况返回 null；不创建业务状态。

不要写成“Calls <= 2 就调用工具，第三次就结束”。那样结束次数由脚本写死，即使 MAF 没有配置轮数限制，实验也可能得到三次调用并错误通过。本例把响应选择交给实际收到的工具列表。

这个客户端每次实验新建，只用于当前串行调用路径，未实现可供并发共享的通用客户端。它不验证真实模型的推理质量、网络或取消传播；流式方法暂时明确抛 NotSupportedException。

## 第二步 接上真实框架循环

如果你是按对话中的小步实现，已有 Calls 与两个响应分支，但尚未添加 FunctionToolCounts，先在 ScriptedChatClient 中补上：

```csharp
public List<int> FunctionToolCounts { get; } = [];
```

在 GetResponseAsync 的 Calls++ 后记录本次提供的函数工具数量：

```csharp
FunctionToolCounts.Add(
    options?.Tools?.OfType<AIFunctionDeclaration>().Count() ?? 0);
```

这样下面的检查分支能编译，并验证最后一次请求确实移除了函数工具。上面的完整参考类已有这项记录，使用完整参考类时不要重复添加。

在 Program.cs 顶部加入 `using FirstAgent;`。将以下分支紧接在 `string mode = args.FirstOrDefault() ?? "stream";` 之后，放在原有模式合法性检查之前；它会在创建真实模型客户端前返回。复用你已有的 MaxToolRounds 常量。

```csharp
if (mode == "loop-check")
{
    var script = new ScriptedChatClient();
    var budget = new ToolCallBudget(2);
    var checkingTools = new ServiceTools(budget);
    using var loop = new FunctionInvokingChatClient(script)
    {
        MaximumIterationsPerRequest = MaxToolRounds
    };
    var checkingAgent = new ChatClientAgent(loop, new ChatClientAgentOptions
    {
        ChatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(checkingTools.GetServiceHealth)]
        }
    });

    AgentResponse response = await checkingAgent.RunAsync("离线循环实验");
    bool passed = script.Calls == 3
        && script.FunctionToolCounts.SequenceEqual(new[] { 1, 1, 0 })
        && budget.Used == 2
        && response.Text == "脚本最终说明";

    Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: "
        + $"Calls={script.Calls}; Used={budget.Used}; "
        + $"tools=[{string.Join(',', script.FunctionToolCounts)}]; "
        + $"text={response.Text}");
    return passed ? 0 : 1;
}
```

程序调用一次 checkingAgent.RunAsync。脚本会被调用几次，由 FunctionInvokingChatClient 的循环决定；这里没有手动 for 循环。

这里使用 checkingTools 和 checkingAgent，避免与当前 Program.cs 外层的 serviceTools 和 agent 同名。

## 第三步 运行与解释

```bash
dotnet build apps/apps.slnx --configuration Release --no-restore
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check
```

当前版本、上述脚本及 MaxToolRounds=2 的预期结果：

```text
PASS: Calls=3; Used=2; tools=[1,1,0]; text=脚本最终说明
```

总退出码应为 0。暂时不要使用 RunStreamingAsync，因为这份参考类只实现了普通响应。

失败实验：临时把 MaxToolRounds 改成 1，重新编译，保留原来的固定断言，应得到 Calls=2、Used=1、tools=[1,0]，打印 FAIL、退出 1；随后恢复配置。

助手已在独立临时项目验证上述普通响应逻辑及失败实验，记录见 [本次学习记录](../progress/2026-11-13-model-round-limit.md)。没有调用模型或 Magpie，没有把参考代码直接写入你的应用。

先完成普通响应，再将响应构造抽为共用方法并实现流式枚举。复述：FunctionCallContent 为什么属于 Assistant 消息？为什么脚本必须查看 options.Tools，而不是根据 Calls 写死第三次结束？

## 第四步 补齐流式响应与检查

前置已满足：本人提交普通入口配置 3 的 FAIL/退出 1 和恢复配置 2 后的 PASS/退出 0。当前只增加流式路径，复用现有普通响应规则，避免重新写一套工具选择与计数逻辑。

### 脚本客户端

在 ScriptedChatClient.cs 中引入 `System.Runtime.CompilerServices`，将现有返回空序列的流式方法替换为下面的形状。保留已验证的 GetResponseAsync；它负责一次 Calls++、一次工具数量记录和一次完整响应构造。

```csharp
public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);
    foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return update;
    }
}
```

这是把一次脚本响应转为更新序列；不会模拟网络分片、时间间隔或逐 token 到达。不要保留原流式方法中的 Calls++，否则调用普通方法后会重复计数。也不要在 foreach 内计数模型请求：多个更新仍可能属于同一次响应。这个小练习不验证网络取消或真实流式性能。

### 检查入口

将当前 `if (mode == "loop-check")` 扩展为：

```csharp
if (mode is "loop-check" or "loop-check-stream")
```

保留这段分支内的脚本客户端、预算、工具和 Agent 构造。用下面的普通/流式选择替换现有的单行 RunAsync；之后继续使用原来的 passed 断言、日志和返回码。

```csharp
AgentResponse response;
if (mode == "loop-check")
{
    response = await checkingAgent.RunAsync("离线循环实验");
}
else
{
    List<AgentResponseUpdate> checkingUpdates = [];
    await foreach (var update in checkingAgent.RunStreamingAsync("离线循环实验"))
    {
        checkingUpdates.Add(update);
    }

    response = checkingUpdates.ToAgentResponse();
}
```

这与 Program.cs 中真实 stream 模式的聚合方式一致，但使用检查分支创建的脚本 Agent，不进入网关配置。循环必须枚举到结束，再比较 Calls、Used、工具数量序列与最终文字。可以在日志里加上 mode 以区分入口；若显示更新总数，把它当作独立观测值，不设成 Calls 的同义词。

### 运行与验收

保持 MaxToolRounds=2，重新编译成功后分别运行：

```bash
dotnet build apps/apps.slnx --configuration Release --no-restore
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check
echo "退出码=$?"
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check-stream
echo "退出码=$?"
```

两种入口都应 PASS、退出 0，且 Calls=3、Used=2、tools=[1,1,0]、text=脚本最终说明。2026-11-17 本人已提交构建成功和两种入口符合该预期的输出，第四步验收通过。本课已有失败实验，本步不要求再次改变轮数。

复述：为什么一次模型响应可以对应多条更新？如果在 yield return 前逐条 Calls++，它测到的还是什么？

上述转换方法在准备时按安装包 XML 文档和现有应用调用方式核对。11 月 17 日助手静态确认本人实现与该方式一致，未重复运行；本人应用运行证据与前面普通示例的助手临时项目验证分开记录。下一步见 [提前完成与受限收尾对照](02-model-round-limit.md#下一小步-提前完成与受限收尾对照)。

2026-11-18：上述后续对照也已完成，四组 FinishReason 断言均通过，见 [验收记录](../progress/2026-11-18-finish-reason.md)。当前进入 [通用请求观测包装层](02-run-observation.md)。
