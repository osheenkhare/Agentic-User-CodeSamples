using System.Runtime.CompilerServices;

namespace HelloWorldStreamingApp;

internal sealed class HelloWorldAgentOrchestrator : IAgentOrchestrator
{
    public async IAsyncEnumerable<IAgentEvent> GetUpdatesAsync(
        Microsoft.Teams.Apps.Schema.MessageActivity activity,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new AgentEvent("Echoing your message", IsInformative: true);

        string fromId = activity.From?.AadObjectId ?? "unknown";
        string agenticUserId = activity.Recipient?.AgenticUserId ?? "unknown";
        string text = activity.Text ?? string.Empty;

        string response =
            $"Received a message from user `{fromId}` to agentic user `{agenticUserId}`, the message text is `{text}`";

        foreach (string word in response.Split(' '))
        {
            // Add deliberate delay of 0.05 seconds to simulate streaming
            // Please remove this delay in production code as it is only for demonstration purposes
            await Task.Delay(TimeSpan.FromSeconds(0.1), cancellationToken);

            yield return new AgentEvent($"{word} ");
        }
    }
}
