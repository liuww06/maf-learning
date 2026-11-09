using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;

// 第 1 课：修改这段指令，观察同一个问题的回答如何变化。
const string Instructions = "你是研发学习助手。可以提供代码审查，代码解释，辅助编码功能。当前未配置网页搜索或浏览工具，不能主动检索最新资料。请使用中文，回答不超过800字。"
    + "说明能力边界。你可以调用 GetServiceHealth 查询虚构服务快照。"
    + "涉及服务状态时先调用工具。"
    + "ok=true 时，仅引用结果中实际存在的状态、观测时间和 evidenceId。"
    + "ok=false 时，报告查询失败及 errorCode，不给出健康状态结论。"
    + "errorCode 不是证据编号。缺失字段应省略或说明未提供，不能补造。"
    + "明确这是练习快照，不能把它说成实时生产状态。工具返回未知服务时如实说明。";

const string DefaultPrompt = "用两句话说明你能如何帮助研发人员排查问题，以及你现在有哪些信息限制。";

string mode = args.FirstOrDefault() ?? "stream";
if (mode is not ("once" or "stream") || args.Length > 2)
{
    Console.Error.WriteLine("用法：dotnet run --project apps/FirstAgent -- [once|stream] [问题]");
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
string prompt = args.ElementAtOrDefault(1) ?? DefaultPrompt;

// 1. SDK 客户端负责协议与连接；Endpoint 保留 /v1，SDK 会选择 /responses 路径。
var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
{
    Endpoint = endpointUri,
    // 首课关闭 SDK 自动重试，便于观察一次请求的成功或失败。
    RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
});

// 2. 将 Responses 客户端适配为 MAF Agent，并注册虚构服务快照查询工具。
// OpenAI 2.14.0 对这些 Responses API 标记了实验性诊断，仅在此处显式接受。
#pragma warning disable OPENAI001
AIAgent agent = client.GetResponsesClient().AsAIAgent(new ChatClientAgentOptions
{
    Name = "DevLearningAssistant",
    ChatOptions = new ChatOptions
    {
        Instructions = Instructions,
        RawRepresentationFactory = _ => new CreateResponseOptions { StoredOutputEnabled = false },
        Tools = [AIFunctionFactory.Create(ServiceTools.GetServiceHealth)]
    }
}, model: model);
#pragma warning restore OPENAI001

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;

var timer = Stopwatch.StartNew();
Console.WriteLine($"协议=Responses 模式={mode} 模型={model} 时限={timeoutSeconds}s");
try
{
    AgentResponse response;
    if (mode == "once")
    {
        // 3a. 等待一次完整的 Agent 响应。
        response = await agent.RunAsync(prompt, cancellationToken: cancellation.Token);
        Console.WriteLine(response.Text);
    }
    else
    {
        // 3b. 持续消费 Agent 更新；更新并不都包含文字。
        List<AgentResponseUpdate> updates = [];
        TimeSpan? firstEvent = null;
        TimeSpan? firstText = null;
        await foreach (var update in agent.RunStreamingAsync(prompt, cancellationToken: cancellation.Token))
        {
            firstEvent ??= timer.Elapsed;
            updates.Add(update);
            if (!string.IsNullOrEmpty(update.Text))
            {
                firstText ??= timer.Elapsed;
                Console.Write(update.Text);
            }
        }

        Console.WriteLine();
        response = updates.ToAgentResponse();
        Console.WriteLine($"Agent更新数={updates.Count} 首更新={Milliseconds(firstEvent)}ms 首文字={Milliseconds(firstText)}ms");
    }

    Console.WriteLine($"完成原因={response.FinishReason?.ToString() ?? "未提供"} 总耗时={timer.ElapsedMilliseconds}ms");
    Console.WriteLine($"Token：输入={response.Usage?.InputTokenCount?.ToString() ?? "未提供"} "
        + $"输出={response.Usage?.OutputTokenCount?.ToString() ?? "未提供"} "
        + $"总量={response.Usage?.TotalTokenCount?.ToString() ?? "未提供"}");
    if (string.IsNullOrWhiteSpace(response.Text))
    {
        Console.Error.WriteLine("本次没有返回可见文字，不能视为本课成功响应。");
        return 1;
    }

    if (response.FinishReason == ChatFinishReason.Length)
    {
        Console.Error.WriteLine("响应因长度限制而截断，不记为完整回答。");
        return 1;
    }

    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"运行已取消或超过时限，耗时={timer.ElapsedMilliseconds}ms；不记为成功。");
    return 130;
}
catch (ClientResultException exception)
{
    Console.Error.WriteLine($"请求失败，HTTP状态={exception.Status}。请检查网关日志、模型路由和认证配置。");
    return 1;
}
catch (HttpRequestException)
{
    Console.Error.WriteLine("无法完成网络请求，请检查 Magpie 网关是否启动、地址是否可达。");
    return 1;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

static string Milliseconds(TimeSpan? elapsed) => elapsed?.TotalMilliseconds.ToString("F0") ?? "未提供";
