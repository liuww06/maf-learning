using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace FirstAgent;

public class ObservedChatClient : DelegatingChatClient
{
    public int Calls { get; private set; } = 0;
    private List<int> _functionToolCounts = [];
    public IReadOnlyList<int> FunctionToolCounts => _functionToolCounts.AsReadOnly();

    public ObservedChatClient(IChatClient innerClient) : base(innerClient)
    {
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecordRequest(options);
        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation]CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecordRequest(options);
        await foreach (var update in base.GetStreamingResponseAsync(
    messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    private void RecordRequest(ChatOptions? options)
    {
        Calls++;
        _functionToolCounts.Add(options?.Tools?.OfType<AIFunctionDeclaration>().Count() ?? 0);
    }



}
