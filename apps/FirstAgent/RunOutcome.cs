using System;
using Microsoft.Extensions.AI;

namespace FirstAgent;

public enum RunStopReason
{
    Completed,
    ToolRoundLimitReached
}
public record RunOutcome(
    RunStopReason Reason,
    string Text,
    int ModelCalls,
    int ToolExecutions,
    ChatFinishReason? ModelFinishReason
);
