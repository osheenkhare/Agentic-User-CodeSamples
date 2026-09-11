using Microsoft.Teams.Apps;
using Microsoft.Teams.Apps.Handlers;
using Microsoft.Teams.Apps.Schema;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddTeamsBotApplication();

WebApplication app = builder.Build();
TeamsBotApplication teams = app.UseTeamsBotApplication();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    sample = "progressive-updates",
}));

teams.OnMessage(async (context, cancellationToken) =>
{
    string command = (
        context.Activity.TextWithoutMentions
        ?? context.Activity.Text
        ?? string.Empty).Trim();

    if (command.Equals("/edit-stream", StringComparison.OrdinalIgnoreCase))
    {
        await EditBasedStreaming.SendAsync(
            context,
            DemoContent.GenerateChunksAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return;
    }

    if (command.Equals("/work-plan", StringComparison.OrdinalIgnoreCase))
    {
        await RunWorkPlanAsync(context, cancellationToken);
        return;
    }

    await context.SendAsync(
        """
        Send one of these commands:

        - `/edit-stream` progressively edits one message.
        - `/work-plan` updates one Adaptive Card as work completes.
        """,
        cancellationToken);
});

await app.RunAsync();

static async Task RunWorkPlanAsync(
    Context<MessageActivity> context,
    CancellationToken cancellationToken)
{
    List<WorkPlanStep> steps =
    [
        new("Understand the request", WorkPlanStatus.InProgress),
        new("Gather the required context", WorkPlanStatus.Pending),
        new("Complete the requested work", WorkPlanStatus.Pending),
        new("Verify the result", WorkPlanStatus.Pending),
    ];

    string conversationId = context.Activity.Conversation?.Id
        ?? throw new InvalidOperationException("Incoming message has no conversation ID.");

    Microsoft.Teams.Core.SendActivityResponse? planActivity = await context.SendAsync(
        WorkPlanCard.CreateMessage(steps),
        cancellationToken);
    string planActivityId = planActivity?.Id
        ?? throw new InvalidOperationException("Teams did not return an activity ID for the work plan.");

    for (int index = 0; index < steps.Count; index++)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
        steps[index].Status = WorkPlanStatus.Done;

        if (index + 1 < steps.Count)
        {
            steps[index + 1].Status = WorkPlanStatus.InProgress;
        }

        await context.Api.Conversations.Activities.UpdateAsync(
            conversationId,
            planActivityId,
            WorkPlanCard.CreateMessage(steps),
            cancellationToken: cancellationToken);
    }

    await context.SendAsync("The planned work is complete.", cancellationToken);
}
