using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.AI;

internal sealed class ConversationAgent(IChatClient chatClient)
{
    private const int MaxConversationMessages = 12;

    private const string SystemPrompt = """
        You are a practical software delivery assistant operating in Microsoft Teams.

        Help engineers design, troubleshoot, and improve CI/CD pipelines, deployments,
        infrastructure as code, observability, incident response, release management, Git
        workflows, containers, Kubernetes, security controls, and operational runbooks.

        Be concise and action-oriented. Prefer numbered steps, commands, configuration snippets,
        validation checks, and rollback guidance when useful. Ask a focused clarification question
        when essential information is missing. Never claim to have inspected or changed a system
        unless a tool result proves it. Clearly identify destructive or production-impacting
        actions and require explicit confirmation before recommending execution.

        Conversation history is isolated by the application before it reaches you. Use only the
        history supplied in the current request. Never claim to remember, retrieve, or infer
        information from another user, 1:1 chat, group chat, channel, or channel thread. If a
        requested personal or conversational detail is absent, say it is not available in the
        current conversation.
        """;

    // A Teams conversation ID is the memory boundary, so chats and channel threads do not share state.
    private readonly ConcurrentDictionary<string, ConversationSession> sessions = new();

    public void Reset(string conversationId)
    {
        sessions.TryRemove(conversationId, out ConversationSession? session);
        session?.Dispose();
    }

    public async Task<string> RespondAsync(
        string conversationId,
        string userText,
        CancellationToken cancellationToken)
    {
        ConversationSession session = sessions.GetOrAdd(conversationId, _ => new());
        await session.Gate.WaitAsync(cancellationToken);

        try
        {
            List<ChatMessage> prompt = BuildPrompt(session.Messages, userText);
            ChatResponse response = await chatClient.GetResponseAsync(
                prompt,
                cancellationToken: cancellationToken);
            string responseText = response.Text?.Trim()
                ?? throw new InvalidOperationException("Azure OpenAI returned an empty response.");

            CommitTurn(session.Messages, userText, responseText);
            return responseText;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task StreamAsync(
        string conversationId,
        string userText,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken)
    {
        ConversationSession session = sessions.GetOrAdd(conversationId, _ => new());
        await session.Gate.WaitAsync(cancellationToken);

        try
        {
            List<ChatMessage> prompt = BuildPrompt(session.Messages, userText);
            StringBuilder response = new();

            await foreach (ChatResponseUpdate update in
                chatClient.GetStreamingResponseAsync(prompt, cancellationToken: cancellationToken))
            {
                if (string.IsNullOrEmpty(update.Text))
                {
                    continue;
                }

                response.Append(update.Text);
                await onChunk(update.Text);
            }

            string responseText = response.ToString().Trim();
            if (responseText.Length == 0)
            {
                throw new InvalidOperationException(
                    "Azure OpenAI returned an empty streaming response.");
            }

            CommitTurn(session.Messages, userText, responseText);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private static List<ChatMessage> BuildPrompt(
        IReadOnlyCollection<ChatMessage> history,
        string userText) =>
        [
            new(ChatRole.System, SystemPrompt),
            .. history,
            new(ChatRole.User, userText),
        ];

    private static void CommitTurn(
        List<ChatMessage> history,
        string userText,
        string responseText)
    {
        history.Add(new ChatMessage(ChatRole.User, userText));
        history.Add(new ChatMessage(ChatRole.Assistant, responseText));

        int excess = history.Count - MaxConversationMessages;
        if (excess > 0)
        {
            history.RemoveRange(0, excess);
        }
    }

    private sealed class ConversationSession : IDisposable
    {
        public List<ChatMessage> Messages { get; } = [];

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void Dispose() => Gate.Dispose();
    }
}
