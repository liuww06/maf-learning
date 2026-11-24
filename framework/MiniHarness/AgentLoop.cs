using Microsoft.Extensions.AI;

namespace MiniHarness;

public sealed class AgentLoopOptions
{
    /// <summary>本次运行可用的工具，每次请求都随 ChatOptions.Tools 发给模型。</summary>
    public required IReadOnlyList<AITool> Tools { get; init; }

    /// <summary>随每次请求发送的指令，对应 ChatOptions.Instructions。</summary>
    public string? Instructions { get; init; }

    /// <summary>允许的工具交互轮数上限。没有默认值，每次运行都要显式给出。</summary>
    public required int MaxToolRounds { get; init; }
}

public static class AgentLoop
{
    // H01：由你实现。直接调用 model.GetResponseAsync，
    // 不使用 FunctionInvokingChatClient，也不使用 ChatClientAgent。
    public static Task<RunOutcome> RunAsync(
        IChatClient model,
        string prompt,
        AgentLoopOptions options,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("H01：请实现 AgentLoop.RunAsync。");
    }
}
