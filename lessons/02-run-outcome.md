# 第 2 课 宿主终止结果：正常返回与达到轮数限制

前置：[模型循环上限](02-model-round-limit.md)、[请求观测包装层](02-run-observation.md) 已完成阶段验证。包装位置失败实验按本人决定跳过，不再加入本课。

状态：2026-11-23 助手准备的任务，关键实现由本人完成。本轮不修改应用代码，也没有新增应用运行结果。

2026-11-24 更新：已搁置。课程评审后转入自研 Harness 主线，不再实现 `ClassifyRun`；`RunOutcome` 契约保留，由 H01 的自有循环直接产出停止原因。见 [评审记录](../progress/2026-11-24-curriculum-review.md)。下文保留为过程记录，不再作为待办。

## 本步要解决的问题

现在默认场景与 early 场景都打印 finish=stop。宿主需要输出一个独立的结束原因，让调用方知道是否走过达到轮数限制后的收尾请求。

本步先覆盖当前离线管线正常返回的两条路径：Completed 和 ToolRoundLimitReached。Completed 仅表示这次运行正常返回、未进入轮数限制的收尾路径，不代表服务健康或业务任务成功。取消、请求失败与回答完整性检查随后接入，不能把那些路径直接归为 Completed。

## 任务一 定义结果契约

新建 `apps/FirstAgent/RunOutcome.cs`，放在 FirstAgent 命名空间；需要引入 Microsoft.Extensions.AI。按下面的形状定义类型：

```csharp
public enum RunStopReason
{
    Completed,
    ToolRoundLimitReached
}

public sealed record RunOutcome(
    RunStopReason Reason,
    string Text,
    int ModelCalls,
    int ToolExecutions,
    ChatFinishReason? ModelFinishReason);
```

Reason 保存宿主判断，ModelFinishReason 原样保存模型响应元数据，Text 保存原回答。两种结束路径都可以有非空文字和 Stop；不要让某一个字段覆盖其他字段，也不要新增含义不清的 IsSuccess。

## 任务二 隔离当前版本的分类规则

在 Program.cs 的静态辅助函数区域增加下面的函数，由你填写函数体：

```csharp
static RunStopReason ClassifyRun(
    int maxToolRounds,
    int calls,
    IReadOnlyList<int> functionToolCounts)
```

函数只接收运行时观测与配置，不接收 early 参数、模型文字、FinishReason 或 ToolCallBudget.Used。early 只控制测试脚本并选择固定预期；Used 达到额度也不能证明函数循环达到轮数上限。

当前适配规则如下：

| 条件 | 行为 |
| --- | --- |
| 配置小于 1、calls 小于 1、记录数量与 calls 不一致、存在负的声明数 | 抛出明确异常，说明输入或观测不合法 |
| 合法记录且 calls 不超过 maxToolRounds | Completed |
| calls 恰好等于 maxToolRounds+1，且最后一次函数声明数量为 0 | ToolRoundLimitReached |
| 请求数超过上述上界，或达到最后请求次数却仍有函数声明 | 抛出 InvalidOperationException，说明观测不符合当前适配规则 |

不要写死 Calls==3；使用传入的 maxToolRounds。计算上界时可用 long，避免 int 加一溢出。

这张表是对**固定版本、已知管线**的适配推断，不是框架公开的终止状态契约。成立条件：Microsoft.Extensions.AI 10.10.0；单层 FunctionInvokingChatClient；ObservedChatClient 紧贴其内层；每次运行新建观测对象；无审批恢复、额外请求重试/缓存/循环包装，且本步只处理已经完整返回的脚本响应。不能把该分类函数不加审查地推广到任意 Agent 管线。

源码依据：普通与流式入口都从 iteration=0 开始，每轮请求内层一次；iteration 达到上限时先移除函数声明，仍请求一次，随后结束函数循环。因此在本课路径中，观察到第 maxToolRounds+1 次请求及零函数声明，可以判断进入了这个收尾分支。只看到零工具、只看到 Stop，或者工具预算已用满，都不足以作出同样判断。

这种分类表示“运行进入了限制收尾分支”，不表示“已证明业务任务失败”，也不声称知道模型若不受限还会做什么。异常与取消期间采集到的部分记录不能送进这个正常返回分类函数。

## 任务三 接入现有离线检查

在 loop-check / loop-check-stream 共用分支中，等 RunAsync 完成或流式完整聚合后：

1. 用 MaxToolRounds、observed.Calls、observed.FunctionToolCounts 调用 ClassifyRun。
2. 构造 RunOutcome，其他字段取自 response.Text、observed.Calls、budget.Used、response.FinishReason。
3. 在原有日志中增加 `reason={outcome.Reason}`。
4. 保留现有计数、声明序列、文字和 FinishReason 断言，增加 Reason 的固定预期。测试可用 early 参数选择预期，分类函数不能读取它。

先在这两个离线检查入口接入；真实 once/stream 的结果处理待取消、失败等路径定义后再统一。现有 FunctionInvokingChatClient 和 ToolCallBudget 仍负责运行时限制，ClassifyRun 只在结束后解释观测，不实施新的准入或终止操作。

## 验收

实现后重新编译 Release，运行四组既有入口，预期如下：

| 场景 | Calls | Used | 声明数 | Reason | ModelFinishReason |
| --- | --- | --- | --- | --- | --- |
| 默认，普通/流式各一次 | 3 | 2 | `[1,1,0]` | ToolRoundLimitReached | Stop |
| early，普通/流式各一次 | 2 | 1 | `[1,1]` | Completed | Stop |

```bash
dotnet build apps/apps.slnx --configuration Release --no-restore
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check early
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check-stream
dotnet run --project apps/FirstAgent --configuration Release --no-build --no-restore -- loop-check-stream early
```

四组均应 PASS、退出 0；每条检查命令后立即记录退出码。这里退出 0 表示“检查结果符合预期”，所以默认场景 Reason=ToolRoundLimitReached 也应 PASS。它不是将真实业务的受限结束自动视为成功。

## 一个失败实验：观测与框架假设不一致

仅在普通默认检查中，临时把传给 ClassifyRun 的声明数替换为测试副本 `[1,1,1]`，Calls 仍为 3、MaxToolRounds 仍为 2。不要修改 observed 的实际记录、预算或包装位置。

预期分类函数抛出 InvalidOperationException，命令退出非 0，不能伪装为 Completed 或 ToolRoundLimitReached。然后恢复真实观测，重新编译并确认该场景 PASS。这个反例检验宿主如何发现适配假设失效，不重复已跳过的包装位置实验。

复述问题：默认场景的 Reason=ToolRoundLimitReached，但 FinishReason=Stop、检查退出码=0，这三个值为什么不矛盾？如果升级框架或改变包装顺序，ClassifyRun 的哪些假设需要重新验证？

## 版本与来源

- [应用锁文件](../apps/FirstAgent/packages.lock.json)：MAF 1.27.0、Microsoft.Extensions.AI 10.10.0；本机该包 nuspec 的 repository commit 为 02107c65bab30aad9e35b5133ed643eaa77bccd8。
- [普通循环源码](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs#L329-L407)、[流式循环源码](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs#L520-L550)、[流式退出条件](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs#L664-L681)、[函数声明过滤](https://github.com/dotnet/extensions/blob/02107c65bab30aad9e35b5133ed643eaa77bccd8/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs#L1055-L1086)：2026-11-23 再次读取固定提交核对。
- [FinishReason 对照证据](../progress/2026-11-18-finish-reason.md)、[观测实现与验证](../progress/2026-11-19-run-observation.md)、[预算与终止知识卡片](../knowledge/04-budgets-and-termination.md)、[个人预算思考](../reflections/001-tool-call-budget.md)：已有证据与复习材料，不代表本课新分类代码已通过测试。
