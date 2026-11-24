using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using MiniHarness;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;

// H01 实验宿主：用自研的 MiniHarness.AgentLoop 跑离线检查和真实模型。
// 骨架由助手搭建（2026-11-24）；check 分支里的检查和 AgentLoop 由本人实现。
string mode = args.FirstOrDefault() ?? "check";

if (mode == "check")
{
    // TODO(H01)：在这里写离线检查。每项打印 PASS/FAIL，全部通过返回 0，否则返回 1。
    Console.Error.WriteLine("H01 尚未实现：请按任务单实现 ScriptedModel、AgentLoop 和本分支里的检查。");
    return 1;
}

if (mode != "run" || args.Length > 2)
{
    Console.Error.WriteLine("用法：dotnet run --project apps/HarnessLab -- [check | run [问题]]");
    return 2;
}

string endpoint = Environment.GetEnvironmentVariable("MAGPIE_BASE_URL") ?? "http://127.0.0.1:3425/v1";
if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri)
    || endpointUri.Scheme is not ("http" or "https"))
{
    Console.Error.WriteLine("MAGPIE_BASE_URL 必须是有效的 HTTP 或 HTTPS 绝对地址。");
    return 2;
}

string timeoutSetting = Environment.GetEnvironmentVariable("MAGPIE_TIMEOUT_SECONDS") ?? "60";
if (!int.TryParse(timeoutSetting, out int timeoutSeconds) || timeoutSeconds is < 1 or > 300)
{
    Console.Error.WriteLine("MAGPIE_TIMEOUT_SECONDS 必须是 1–300 的整数。");
    return 2;
}

string model = Environment.GetEnvironmentVariable("MAGPIE_MODEL") ?? "zcode/GLM-5.3-Flash";
// SDK 要求一个非空凭据。此占位值不是真实密钥；网关若开启认证，使用 MAGPIE_API_KEY。
string apiKey = Environment.GetEnvironmentVariable("MAGPIE_API_KEY") ?? "local-placeholder";
string prompt = args.ElementAtOrDefault(1) ?? "请调用工具查询 order-api 的快照状态，并给出观测时间和证据编号。";

const string Instructions = "你可以调用 GetServiceHealth 查询虚构服务快照。"
    + "涉及服务状态时先调用工具。"
    + "ok=true 时，仅引用结果中实际存在的状态、观测时间和 evidenceId。"
    + "ok=false 时，报告查询失败及 errorCode，不给出健康状态结论。"
    + "errorCode 不是证据编号。缺失字段应省略或说明未提供，不能补造。"
    + "明确这是练习快照，不能把它说成实时生产状态。工具返回未知服务时如实说明。";

// SDK 客户端负责协议与连接；关闭自动重试，便于数清模型请求次数。
var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
{
    Endpoint = endpointUri,
    RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
});

// 裸模型客户端：只负责协议和请求选项，不自动执行工具。工具循环由 AgentLoop 负责。
// OpenAI 2.14.0 把 Responses API 标为实验性（OPENAI001），MAF 1.27.0 把这个适配方法标为实验性（MAAI001）。
#pragma warning disable OPENAI001, MAAI001
IChatClient chatModel = client.GetResponsesClient()
    .AsIChatClientWithStoredOutputDisabled(model, includeReasoningEncryptedContent: false);
#pragma warning restore OPENAI001, MAAI001

var budget = new ToolCallBudget(2);
var serviceTools = new ServiceTools(budget);
var options = new AgentLoopOptions
{
    Instructions = Instructions,
    Tools = [AIFunctionFactory.Create(serviceTools.GetServiceHealth)],
    MaxToolRounds = 2
};

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
var timer = Stopwatch.StartNew();
Console.WriteLine($"协议=Responses 模型={model} 时限={timeoutSeconds}s 工具轮数上限={options.MaxToolRounds} 工具执行上限={budget.Limit}");
try
{
    RunOutcome outcome = await AgentLoop.RunAsync(chatModel, prompt, options, cancellation.Token);
    Console.WriteLine(outcome.Text);
    Console.WriteLine($"停止原因={outcome.Reason} 模型请求={outcome.ModelCalls} 工具执行={outcome.ToolExecutions} "
        + $"模型完成原因={outcome.ModelFinishReason?.ToString() ?? "未提供"} 总耗时={timer.ElapsedMilliseconds}ms");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"运行已取消或超过时限，耗时={timer.ElapsedMilliseconds}ms。");
    return 130;
}
