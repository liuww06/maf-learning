using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace FirstAgent;

public class ScriptedChatClient : IChatClient
{
    private bool _useEarlyMode = false;
    public ScriptedChatClient(bool useEarlyMode = false)
    {
        _useEarlyMode = useEarlyMode;
    }
    public List<int> FunctionToolCounts { get; } = [];
    public int Calls { get; private set; } = 0;
    public void Dispose()
    {
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        FunctionToolCounts.Add(options?.Tools?.OfType<AIFunctionDeclaration>().Count() ?? 0);

        if (Calls >= 6)
            throw new InvalidOperationException("脚本调用达到 6 次，请检查轮数限制是否接入。");


        var toolName = "GetServiceHealth";

        bool canCall = options?.Tools?.OfType<AIFunctionDeclaration>()
            .Any(f => f.Name == toolName) is true;

        if (canCall is not true)
        {
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "脚本最终说明"))
            {
                FinishReason = ChatFinishReason.Stop
            });
        }

        var hasToolResult = messages.SelectMany(x=>x.Contents).OfType<FunctionResultContent>().Any();

        if(_useEarlyMode && hasToolResult)
        {
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "脚本正常结束"))
            {
                FinishReason = ChatFinishReason.Stop
            });
        }

        var call = new FunctionCallContent(
            $"call-{Calls}",
            toolName,
            new Dictionary<string, object?>()
            {
                ["serviceId"] = "order-api"
            });
        var message = new ChatMessage(ChatRole.Assistant, [call]);
        return Task.FromResult(new ChatResponse(message)
        {
            FinishReason = ChatFinishReason.ToolCalls
        });
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage>
        messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }
    }
}
