using Microsoft.Extensions.AI;

namespace MiniHarness;

/// <summary>一次 Agent 运行的宿主侧结果。停止原因由循环直接给出，不从模型输出反推。</summary>
public enum RunStopReason
{
    /// <summary>模型返回了没有工具调用的响应。</summary>
    Completed,

    /// <summary>工具交互轮数达到上限，循环停止。</summary>
    ToolRoundLimitReached
}

/// <param name="Reason">宿主判断的停止原因。</param>
/// <param name="Text">最后一条模型响应的文字；没有最终文字时为空字符串。</param>
/// <param name="ModelCalls">循环实际发出的模型请求次数。</param>
/// <param name="ToolExecutions">循环实际执行的工具调用次数，未知工具不计。</param>
/// <param name="ModelFinishReason">最后一次模型响应的完成原因，原样保存。</param>
public record RunOutcome(
    RunStopReason Reason,
    string Text,
    int ModelCalls,
    int ToolExecutions,
    ChatFinishReason? ModelFinishReason
);
