using System.Runtime.CompilerServices;

namespace GraphApiStreamingApp;

internal sealed class GraphAgentOrchestrator : IAgentOrchestrator
{
    private readonly GraphService _graphService;

    public GraphAgentOrchestrator(GraphService graphService) =>
        _graphService = graphService;

    public async IAsyncEnumerable<IAgentEvent> GetUpdatesAsync(
        Microsoft.Teams.Apps.Schema.MessageActivity activity,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new AgentEvent("Fetching user details from Graph Api", IsInformative: true);

        string userId = activity.From?.AadObjectId ?? string.Empty;

        UserProfile profile = await _graphService.GetUserProfileAsync(
            userId,
            cancellationToken);

        string message =
            $"Hi `{profile.DisplayName}`, your email fetched from graph call is `{profile.Email}`";

        foreach (string word in message.Split(' '))
        {
            // Add deliberate delay of 0.1 seconds to simulate streaming
            // Please remove this delay in production code as it is only for demonstration purposes
            await Task.Delay(TimeSpan.FromSeconds(0.1), cancellationToken);

            yield return new AgentEvent($"{word} ");
        }
    }
}
