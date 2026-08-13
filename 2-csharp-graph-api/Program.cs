using Microsoft.Teams.Apps;
using Microsoft.Teams.Apps.Handlers;
using GraphApiStreamingApp;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddTeamsBotApplication();

GraphService graphService = GraphService.Create(builder.Configuration);

IAgentOrchestrator agent = new GraphAgentOrchestrator(graphService);

WebApplication app = builder.Build();
TeamsBotApplication teams = app.UseTeamsBotApplication();

teams.OnMessage(async (context, cancellationToken) =>
{
    TeamsStreamingWriter stream = TeamsStreamingWriter.CreateFromContext(context);

    bool streamingStarted = false;

    await foreach (IAgentEvent update in agent.GetUpdatesAsync(
        context.Activity,
        cancellationToken))
    {
        if (update.IsInformative)
        {
            // Teams disallows informative updates once response streaming has begun.
            if (streamingStarted)
            {
                continue;
            }

            await stream.SendInformativeUpdateAsync(update.Text, cancellationToken);
        }
        else if (update.Text.Length > 0)
        {
            streamingStarted = true;
            await stream.AppendResponseAsync(update.Text, cancellationToken);
        }
    }

    await stream.FinalizeResponseAsync(cancellationToken: cancellationToken);
});

await app.RunAsync();
