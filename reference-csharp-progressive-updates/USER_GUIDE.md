# Progressive updates user guide

The lab separates the reusable behavior from the demo message handler:

| File | Purpose |
| --- | --- |
| `EditBasedStreaming.cs` | Replaces one message with accumulated response text at a bounded cadence. |
| `WorkPlanCard.cs` | Builds a collapsible Adaptive Card from a collection of work-plan steps. |
| `DemoContent.cs` | Produces fake response chunks for the runnable demonstration. |
| `Program.cs` | Shows how to call both reusable components from a Teams handler. |

## Reuse edit-based streaming

Pass an `IAsyncEnumerable<string>` from a model or any incremental producer:

```csharp
await EditBasedStreaming.SendAsync(
    context,
    modelChunks,
    updateInterval: TimeSpan.FromSeconds(1),
    placeholder: "Working...",
    cancellationToken);
```

`EditBasedStreaming` sends the placeholder, retains its activity ID, and calls
`context.Api.Conversations.Activities.UpdateAsync` with the complete accumulated
text. Updates are awaited sequentially, so an older update cannot overwrite a
newer one.

Do not send an update for every token. A one- or two-second interval is a
practical starting point for reducing flicker and API traffic.

This is regular message editing rather than native Teams streaming. Teams may
show an edited indicator, and updates remain subject to normal service
throttling.

## Reuse the work-plan card

Create a mutable collection of short, user-visible steps:

```csharp
List<WorkPlanStep> steps =
[
    new("Understand the request", WorkPlanStatus.InProgress),
    new("Complete the work", WorkPlanStatus.Pending),
    new("Verify the result", WorkPlanStatus.Pending),
];
```

Send the initial card and retain the activity ID:

```csharp
var sent = await context.SendAsync(
    WorkPlanCard.CreateMessage(steps),
    cancellationToken);
```

When work advances, update the statuses and replace the original activity:

```csharp
steps[0].Status = WorkPlanStatus.Done;
steps[1].Status = WorkPlanStatus.InProgress;

await context.Api.Conversations.Activities.UpdateAsync(
    context.Activity.Conversation!.Id!,
    sent!.Id!,
    WorkPlanCard.CreateMessage(steps),
    cancellationToken: cancellationToken);
```

The entire card is replaced on every update, so always render it from the
complete current step collection. Completed plans start collapsed and can be
reopened from the card header.

The card is a user-visible work-status summary, not model chain-of-thought.
