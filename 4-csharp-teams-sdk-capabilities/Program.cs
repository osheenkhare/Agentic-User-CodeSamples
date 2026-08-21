using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Client;
using Microsoft.Teams.Apps;
using Microsoft.Teams.Apps.Handlers;
using Microsoft.Teams.Apps.Schema;
using Microsoft.Teams.Apps.Schema.Entities;
using Microsoft.Teams.Core.Schema;
using System.ClientModel;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

string azureOpenAiEndpoint = RequireConfiguration(builder.Configuration, "AzureOpenAI:Endpoint");
string azureOpenAiApiKey = RequireConfiguration(builder.Configuration, "AzureOpenAI:ApiKey");
string azureOpenAiDeployment = RequireConfiguration(builder.Configuration, "AzureOpenAI:Deployment");
string tenantId = RequireConfiguration(builder.Configuration, "AzureAd:TenantId");
string agentAppId = RequireConfiguration(builder.Configuration, "AzureAd:ClientId");
string githubToken = RequireConfiguration(builder.Configuration, "GitHub:Token");
string githubDefaultRepository =
    builder.Configuration["GitHub:DefaultRepository"] ?? "microsoft/teams.net";

builder.Services.AddTeamsBotApplication();
builder.Services.AddSingleton<IChatClient>(_ =>
    new AzureOpenAIClient(
            new Uri(azureOpenAiEndpoint),
            new ApiKeyCredential(azureOpenAiApiKey))
        .GetChatClient(azureOpenAiDeployment)
        .AsIChatClient());
builder.Services.AddSingleton<ConversationAgent>();
builder.Services.AddSingleton<ActionItemStore>();
builder.Services.AddSingleton<ConversationScopeResolver>();
builder.Services.AddSingleton<ScopedMemoryStore>();
builder.Services.AddSingleton<MemoryActionScopeStore>();
builder.Services.AddHttpClient<GitHubActionsService>();
builder.Services.AddSingleton(serviceProvider =>
    new GitHubActionsService(
        serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(),
        githubToken));
builder.Services.AddSingleton<BackgroundTaskQueue>();
builder.Services.AddHostedService(
    serviceProvider => serviceProvider.GetRequiredService<BackgroundTaskQueue>());

WebApplication app = builder.Build();
TeamsBotApplication teams = app.UseTeamsBotApplication();
ConversationAgent conversationAgent = app.Services.GetRequiredService<ConversationAgent>();
ActionItemStore actionItems = app.Services.GetRequiredService<ActionItemStore>();
ConversationScopeResolver memoryScopeResolver =
    app.Services.GetRequiredService<ConversationScopeResolver>();
ScopedMemoryStore scopedMemories =
    app.Services.GetRequiredService<ScopedMemoryStore>();
MemoryActionScopeStore memoryActionScopes =
    app.Services.GetRequiredService<MemoryActionScopeStore>();
GitHubActionsService githubActions = app.Services.GetRequiredService<GitHubActionsService>();
BackgroundTaskQueue backgroundTasks = app.Services.GetRequiredService<BackgroundTaskQueue>();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    app = "teams-sdk-capabilities",
    sdkVersion = "2.1.0-preview-0011",
    dotnetVersion = Environment.Version.ToString(),
}));

teams.OnMessage(async (context, cancellationToken) =>
{
    string conversationId = context.Activity.Conversation?.Id
        ?? throw new InvalidOperationException("Incoming message has no conversation ID.");
    string conversationType = context.Activity.Conversation?.ConversationType
        ?? ConversationTypes.Personal;
    string userText = NormalizeUserText(
        context.Activity.TextWithoutMentions
        ?? context.Activity.Text
        ?? string.Empty);
    string activityId = context.Activity.Id
        ?? throw new InvalidOperationException("Incoming message has no activity ID.");
    string requesterId = context.Activity.From?.Id
        ?? throw new InvalidOperationException("Incoming message has no sender ID.");
    ConversationMemoryScope memoryScope;
    try
    {
        memoryScope = ResolveMemoryScope(
            memoryScopeResolver,
            tenantId,
            agentAppId,
            conversationType,
            conversationId,
            requesterId,
            context.Activity.Recipient?.Id,
            context.Activity.ChannelData?.TeamsTeamId
                ?? context.Activity.ChannelData?.Team?.Id,
            context.Activity.ChannelData?.TeamsChannelId
                ?? context.Activity.ChannelData?.Channel?.Id,
            context.Activity.ReplyToId,
            activityId);
    }
    catch (ArgumentException exception)
    {
        await SendInCurrentContextAsync(
            $"I could not establish a safe memory boundary: {exception.Message}");
        return;
    }

    if (userText.Equals("/reset", StringComparison.OrdinalIgnoreCase))
    {
        conversationAgent.Reset(conversationId);
        actionItems.Clear(conversationId);
        int removed = scopedMemories.Clear(memoryScope);
        await SendInCurrentContextAsync(
            $"Session memory and saved action items cleared. Removed {removed} explicit item(s) from {memoryScope.Label} only.");
        return;
    }

    if (userText.Equals("/memory-demo", StringComparison.OrdinalIgnoreCase))
    {
        await SendActivityInCurrentContextAsync(
            MemoryCards.CreateIntroMessage(
                memoryActionScopes.Create(
                    memoryScope,
                    requesterId,
                    conversationId).Id));
        return;
    }

    if (userText.Equals("/memory", StringComparison.OrdinalIgnoreCase))
    {
        await SendActivityInCurrentContextAsync(
            MemoryCards.CreateStatusMessage(
                memoryScope,
                scopedMemories.Get(memoryScope)));
        return;
    }

    if (userText.Equals("/forget", StringComparison.OrdinalIgnoreCase))
    {
        await SendActivityInCurrentContextAsync(
            MemoryCards.CreateForgetConfirmationMessage(
                memoryScope,
                scopedMemories.Get(memoryScope).Count,
                memoryActionScopes.Create(
                    memoryScope,
                    requesterId,
                    conversationId).Id));
        return;
    }

    Match remember = Regex.Match(
        userText,
        @"^\s*(?:please\s+)?remember(?:\s+that)?\s+(?<fact>.+?)\s*[.!]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    if (remember.Success)
    {
        try
        {
            MemoryFact fact = scopedMemories.Add(
                memoryScope,
                remember.Groups["fact"].Value);
            await SendActivityInCurrentContextAsync(
                MemoryCards.CreateSavedMessage(memoryScope, fact));
        }
        catch (ArgumentException exception)
        {
            await SendInCurrentContextAsync(exception.Message);
        }
        return;
    }

    if (IsScopedMemoryRecallRequest(userText))
    {
        IReadOnlyList<MemoryFact> facts = scopedMemories.Get(memoryScope);
        if (facts.Count > 0)
        {
            await SendInCurrentContextAsync(
                CreateScopedMemoryRecallResponse(memoryScope, facts));
            return;
        }
    }

    if (userText.Equals("/actions", StringComparison.OrdinalIgnoreCase))
    {
        IReadOnlyList<string> items = actionItems.Get(conversationId);
        string response = items.Count == 0
            ? "No saved action items. Add a 📌 reaction to an agent response to save one."
            : string.Join(
                Environment.NewLine,
                new[] { "Saved action items:", "" }
                    .Concat(items.Select((item, index) => $"{index + 1}. {item}")));
        await SendInCurrentContextAsync(response);
        return;
    }

    if (string.IsNullOrWhiteSpace(userText))
    {
        await SendInCurrentContextAsync("Tell me which software delivery workflow you want help with.");
        return;
    }

    if (userText.Equals(
        "show latest GitHub Actions analysis",
        StringComparison.OrdinalIgnoreCase))
    {
        if (!githubActions.TryGetLatest(conversationId, out GitHubActionsAnalysis? analysis)
            || analysis is null)
        {
            await SendInCurrentContextAsync(
                "No completed GitHub Actions analysis is available yet. Send `/github` to start one.");
            return;
        }

        await SendActivityInCurrentContextAsync(CreateGitHubAnalysisMessage(analysis));
        return;
    }

    if (IsGitHubActionsRequest(userText))
    {
        await SendActivityInCurrentContextAsync(
            CreateGitHubClarificationCard(githubDefaultRepository));
        return;
    }

    AgenticIdentity? agenticIdentity = context.Activity.Recipient?.GetAgenticIdentity();
    await TryAddReactionAsync("holdon");
    string completionReaction = "2705_whiteheavycheckmark";

    if (conversationType.Equals(ConversationTypes.Personal, StringComparison.OrdinalIgnoreCase))
    {
        TeamsStreamingWriter writer = TeamsStreamingWriter.CreateFromContext(context);

        // Informative updates must precede the first streamed response chunk.
        await writer.SendInformativeUpdateAsync(
            "Analyzing the software delivery workflow…",
            cancellationToken);

        try
        {
            await conversationAgent.StreamAsync(
                conversationId,
                userText,
                chunk => writer.AppendResponseAsync(chunk, cancellationToken),
                cancellationToken);

            MessageActivity finalMessage = new();
            DecorateAiResponse(finalMessage, userText);
            await writer.FinalizeResponseAsync(finalMessage, cancellationToken);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            completionReaction = "2757_heavyexclamationmarksymbol";
            MessageActivity rateLimitMessage = new(
                "The AI model is temporarily rate-limited. Please retry in about one minute.");
            rateLimitMessage.AddAIGenerated();
            await writer.FinalizeResponseAsync(rateLimitMessage, cancellationToken);
        }
    }
    else
    {
        try
        {
            string response = await conversationAgent.RespondAsync(
                conversationId,
                userText,
                cancellationToken);

            MessageActivity reply = new(response);
            DecorateAiResponse(reply, userText);
            await context.ReplyAsync(reply, cancellationToken);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            completionReaction = "2757_heavyexclamationmarksymbol";
            await context.ReplyAsync(
                "The AI model is temporarily rate-limited. Please retry in about one minute.",
                cancellationToken);
        }
    }

    await TryAddReactionAsync(completionReaction);

    Task SendInCurrentContextAsync(string text) =>
        conversationType.Equals(ConversationTypes.Personal, StringComparison.OrdinalIgnoreCase)
            ? context.SendAsync(text, cancellationToken)
            : context.ReplyAsync(text, cancellationToken);

    Task SendActivityInCurrentContextAsync(MessageActivity activity) =>
        conversationType.Equals(ConversationTypes.Personal, StringComparison.OrdinalIgnoreCase)
            ? context.SendAsync(activity, cancellationToken)
            : context.ReplyAsync(activity, cancellationToken);

    async Task<bool> TryAddReactionAsync(string reactionType)
    {
        if (agenticIdentity is null)
        {
            Console.Error.WriteLine(
                "[reaction] skipped because inbound activity has no agentic identity.");
            return false;
        }

        try
        {
            // Reaction calls use the AgenticIdentity from the inbound activity, not app-only auth.
            await context.Api.Conversations.Reactions.AddAsync(
                conversationId,
                activityId,
                reactionType,
                agenticIdentity,
                cancellationToken: cancellationToken);
            return true;
        }
        catch (HttpRequestException exception)
        {
            Console.Error.WriteLine(
                "[reaction] add failed id={0} type={1}: {2}",
                activityId,
                reactionType,
                exception.Message);
            return false;
        }
        catch (MsalServiceException exception)
        {
            Console.Error.WriteLine(
                "[reaction] token acquisition failed id={0} type={1}: {2}",
                activityId,
                reactionType,
                exception.ErrorCode);
            return false;
        }
    }
});

teams.OnMessageReactionAdded(async (context, cancellationToken) =>
{
    string conversationId = context.Activity.Conversation?.Id
        ?? throw new InvalidOperationException("Reaction activity has no conversation ID.");
    string conversationType = context.Activity.Conversation?.ConversationType
        ?? ConversationTypes.Personal;
    string? reactingUserId = context.Activity.From?.Id;
    string? agentId = context.Activity.Recipient?.Id;
    AgenticIdentity? agenticIdentity = context.Activity.Recipient?.GetAgenticIdentity();
    if (!string.IsNullOrWhiteSpace(reactingUserId)
        && reactingUserId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    GitHubActionsAnalysis? analysis = githubActions.TryGetLatest(
        conversationId,
        out GitHubActionsAnalysis? latestAnalysis)
        ? latestAnalysis
        : null;
    string contextSummary = analysis?.ToReport()
        ?? "Use the recent conversation context to determine the relevant task.";

    foreach (MessageReaction reaction in context.Activity.ReactionsAdded ?? [])
    {
        string? trigger = NormalizeReactionTrigger(reaction.Type);
        if (trigger == "like")
        {
            await TryAcknowledgeTriggerAsync();
            Console.WriteLine(
                "[feedback] positive conversation={0} message={1}",
                conversationId,
                context.Activity.ReplyToId ?? "unknown");
            await SendReactionResponseAsync("Thanks — positive feedback recorded.");
            continue;
        }

        string? instruction = trigger switch
        {
            "checkmark" =>
                "Create a concise implementation checklist for the work under discussion. Include validation and rollback checks.",
            "pushpin" =>
                "Summarize the single most important next action as one concise, standalone action item. Do not add a heading.",
            "exclamation" =>
                "Create a concise remediation plan covering immediate risks, rollback steps, validation, and escalation criteria.",
            _ => null,
        };
        if (instruction is null)
        {
            continue;
        }

        await TryAcknowledgeTriggerAsync();

        try
        {
            string response = await conversationAgent.RespondAsync(
                conversationId,
                $"""
                A user added a {trigger} reaction to an agent response.

                {instruction}

                Relevant tool context:
                {contextSummary}
                """,
                cancellationToken);

            if (trigger == "pushpin")
            {
                actionItems.Add(conversationId, response);
                response = $"📌 Saved action item:{Environment.NewLine}{Environment.NewLine}{response}{Environment.NewLine}{Environment.NewLine}Send `/actions` to review saved items.";
            }

            MessageActivity message = new(response);
            DecorateAiResponse(message, instruction);
            await SendReactionActivityAsync(message);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            await SendReactionResponseAsync(
                "The AI model is temporarily rate-limited. Please retry the reaction in about one minute.");
        }
    }

    Task SendReactionResponseAsync(string text) =>
        conversationType.Equals(ConversationTypes.Personal, StringComparison.OrdinalIgnoreCase)
            ? context.SendAsync(text, cancellationToken)
            : context.ReplyAsync(text, cancellationToken);

    Task SendReactionActivityAsync(MessageActivity activity) =>
        conversationType.Equals(ConversationTypes.Personal, StringComparison.OrdinalIgnoreCase)
            ? context.SendAsync(activity, cancellationToken)
            : context.ReplyAsync(activity, cancellationToken);

    async Task TryAcknowledgeTriggerAsync()
    {
        string? targetMessageId = context.Activity.ReplyToId ?? context.Activity.Id;
        if (agenticIdentity is null || string.IsNullOrWhiteSpace(targetMessageId))
        {
            return;
        }

        try
        {
            await context.Api.Conversations.Reactions.AddAsync(
                conversationId,
                targetMessageId,
                "1f440_eyes",
                agenticIdentity,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or MsalServiceException)
        {
            Console.Error.WriteLine(
                "[reaction] acknowledgement failed message={0}: {1}",
                targetMessageId,
                exception.Message);
        }
    }
});

teams.OnAdaptiveCardAction(async (context, cancellationToken) =>
{
    // Action.Execute data routes this card submission independently of normal messages.
    Dictionary<string, object>? data = context.Activity.Value?.Action?.Data;
    if (data is null)
    {
        return AdaptiveCardResponse.CreateMessageResponse("No action data was supplied.", 400);
    }

    string? action = GetCardValue(data, "action");
    if (action is "memory_demo_save"
        or "memory_demo_inspect"
        or "memory_forget_confirm"
        or "memory_forget_cancel")
    {
        string requesterId = context.Activity.From?.Id ?? string.Empty;
        string memoryConversationId =
            context.Activity.Conversation?.Id ?? string.Empty;
        string? memoryActionScopeId =
            GetCardValue(data, "memoryActionScopeId");
        if (string.IsNullOrWhiteSpace(memoryActionScopeId)
            || !memoryActionScopes.TryConsume(
                memoryActionScopeId,
                requesterId,
                memoryConversationId,
                out ConversationMemoryScope? memoryScope)
            || memoryScope is null)
        {
            return AdaptiveCardResponse.CreateMessageResponse(
                "This memory card is expired, already used, belongs to another user, or came from another conversation.",
                200);
        }

        if (action == "memory_demo_save")
        {
            MemoryFact fact = scopedMemories.Add(
                memoryScope,
                "my project codename is Bluebird");
            return AdaptiveCardResponse.CreateCardResponse(
                MemoryCards.CreateSavedCard(memoryScope, fact));
        }

        if (action == "memory_demo_inspect")
        {
            return AdaptiveCardResponse.CreateCardResponse(
                MemoryCards.CreateStatusCard(
                    memoryScope,
                    scopedMemories.Get(memoryScope)));
        }

        if (action == "memory_forget_confirm")
        {
            int removed = scopedMemories.Clear(memoryScope);
            conversationAgent.Reset(memoryConversationId);
            return AdaptiveCardResponse.CreateCardResponse(
                MemoryCards.CreateClearedCard(memoryScope, removed));
        }

        return AdaptiveCardResponse.CreateCardResponse(
            MemoryCards.CreateCancelledCard(memoryScope));
    }

    if (!string.Equals(action, "analyze_github_actions", StringComparison.Ordinal))
    {
        return AdaptiveCardResponse.CreateMessageResponse("Unknown action.", 400);
    }

    string repository = GetCardValue(data, "repository") ?? githubDefaultRepository;
    string? branch = GetCardValue(data, "branch");
    string status = GetCardValue(data, "status") ?? "failure";
    if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
    {
        return AdaptiveCardResponse.CreateMessageResponse(
            "Repository must use the `owner/name` format.",
            400);
    }

    if (status is not ("all" or "failure" or "in_progress"))
    {
        return AdaptiveCardResponse.CreateMessageResponse("Invalid run status.", 400);
    }

    int lookbackDays = int.TryParse(GetCardValue(data, "lookbackDays"), out int parsedDays)
        ? Math.Clamp(parsedDays, 1, 30)
        : 7;
    string conversationId = context.Activity.Conversation?.Id
        ?? throw new InvalidOperationException("Card action has no conversation ID.");
    Uri? serviceUrl = context.Activity.ServiceUrl;
    AgenticIdentity? agenticIdentity = context.Activity.Recipient?.GetAgenticIdentity();

    await context.SendAsync(
        $"Started GitHub Actions analysis for `{repository}`. I will notify this conversation when it completes.",
        cancellationToken);

    await backgroundTasks.EnqueueAsync(async backgroundCancellationToken =>
    {
        string notification;
        try
        {
            GitHubActionsAnalysis analysis = await githubActions.AnalyzeAsync(
                conversationId,
                repository,
                branch,
                status,
                lookbackDays,
                backgroundCancellationToken);
            notification = analysis.ToNotification();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[github] background analysis failed: {0}", exception);
            notification =
                $"GitHub Actions analysis failed for `{repository}`: {exception.Message}";
        }

        try
        {
            // Captured routing and identity data allow proactive completion after the invoke ends.
            await teams.SendAsync(
                conversationId,
                notification,
                serviceUrl,
                agenticIdentity,
                backgroundCancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[github] proactive notification failed: {0}", exception);
        }
    }, cancellationToken);

    return AdaptiveCardResponse.CreateMessageResponse("Analysis started.");
});

teams.OnMessageSubmitFeedback((context, cancellationToken) =>
{
    Console.WriteLine(
        "[feedback] message={0} reaction={1} feedback={2}",
        context.Activity.ReplyToId ?? "unknown",
        context.Activity.Value?.Reaction ?? "unknown",
        context.Activity.Value?.Feedback ?? string.Empty);

    return Task.FromResult(new InvokeResponse(200, new { accepted = true }));
});

await app.RunAsync();

static string RequireConfiguration(IConfiguration configuration, string key)
{
    string? value = configuration[key];
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Missing required configuration: {key}");
}

static string NormalizeUserText(string text)
{
    string withoutHtml = Regex.Replace(WebUtility.HtmlDecode(text), "<[^>]+>", " ");
    string withoutFormattingCharacters = new(
        withoutHtml
            .Where(character =>
                !char.IsControl(character)
                && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format)
            .ToArray());

    return Regex.Replace(withoutFormattingCharacters, @"\s+", " ").Trim();
}

static bool IsGitHubActionsRequest(string text) =>
    text.Equals("/github", StringComparison.OrdinalIgnoreCase)
    || text.Contains("github actions", StringComparison.OrdinalIgnoreCase)
    || text.Contains("workflow runs", StringComparison.OrdinalIgnoreCase);

static bool IsScopedMemoryRecallRequest(string text) =>
    Regex.IsMatch(
        text,
        @"\b(?:what|which|recall|remember)\b.{0,80}\b(?:code\s*name|codename|stored\s+memory|shared\s+release\s+window)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

static string CreateScopedMemoryRecallResponse(
    ConversationMemoryScope scope,
    IReadOnlyList<MemoryFact> facts) =>
    $"""
    In the active {scope.Label} memory scope (Scope ID: {scope.DisplayId}), I have:

    {string.Join(Environment.NewLine, facts.Select((fact, index) => $"{index + 1}. {fact.Text}"))}
    """;

static ConversationMemoryScope ResolveMemoryScope(
    ConversationScopeResolver resolver,
    string tenantId,
    string configuredAgentId,
    string conversationType,
    string conversationId,
    string userId,
    string? activityAgentId,
    string? teamId,
    string? channelId,
    string? replyToId,
    string activityId)
{
    bool isChannel = conversationType.Equals(
        "channel",
        StringComparison.OrdinalIgnoreCase);
    return resolver.Resolve(new ConversationScopeInput(
        tenantId,
        conversationType,
        conversationId,
        userId,
        activityAgentId ?? configuredAgentId,
        teamId,
        isChannel ? GetChannelId(conversationId, channelId) : null,
        isChannel
            ? GetChannelRootMessageId(conversationId, replyToId, activityId)
            : null));
}

static string? GetChannelId(string conversationId, string? channelId)
{
    if (!string.IsNullOrWhiteSpace(channelId))
    {
        return channelId;
    }

    int messageSuffix = conversationId.IndexOf(
        ";messageid=",
        StringComparison.OrdinalIgnoreCase);
    string candidate = messageSuffix >= 0
        ? conversationId[..messageSuffix]
        : conversationId;
    return !string.IsNullOrWhiteSpace(candidate) ? candidate : null;
}

static string GetChannelRootMessageId(
    string conversationId,
    string? replyToId,
    string activityId)
{
    Match root = Regex.Match(
        conversationId,
        @"(?:^|;)messageid=(?<messageId>[^;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    return root.Success
        ? Uri.UnescapeDataString(root.Groups["messageId"].Value)
        : !string.IsNullOrWhiteSpace(replyToId)
            ? replyToId
            : activityId;
}

static MessageActivity CreateGitHubClarificationCard(string defaultRepository)
{
    JsonElement card = JsonSerializer.Deserialize<JsonElement>(
        $$"""
        {
          "type": "AdaptiveCard",
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "version": "1.5",
          "body": [
            {
              "type": "TextBlock",
              "text": "Inspect GitHub Actions",
              "weight": "Bolder",
              "size": "Medium"
            },
            {
              "type": "Input.Text",
              "id": "repository",
              "label": "Repository",
              "value": {{JsonSerializer.Serialize(defaultRepository)}},
              "isRequired": true
            },
            {
              "type": "Input.ChoiceSet",
              "id": "status",
              "label": "Run status",
              "value": "failure",
              "choices": [
                { "title": "All runs", "value": "all" },
                { "title": "Failed runs", "value": "failure" },
                { "title": "In-progress runs", "value": "in_progress" }
              ]
            },
            {
              "type": "Input.Text",
              "id": "branch",
              "label": "Branch (optional)",
              "placeholder": "main"
            },
            {
              "type": "Input.Number",
              "id": "lookbackDays",
              "label": "Lookback days",
              "value": 7,
              "min": 1,
              "max": 30
            }
          ],
          "actions": [
            {
              "type": "Action.Execute",
              "title": "Analyze",
              "verb": "analyze_github_actions",
              "data": {
                "action": "analyze_github_actions"
              }
            }
          ]
        }
        """);

    TeamsAttachment attachment = TeamsAttachment.CreateBuilder()
        .WithAdaptiveCard(card)
        .Build();

    MessageActivity message = new();
    message.AddAttachment(attachment);
    return message;
}

static MessageActivity CreateGitHubAnalysisMessage(GitHubActionsAnalysis analysis)
{
    MessageActivity message = new(analysis.ToReport());

    // These schema extensions light up AI metadata, feedback, citations, and prompt buttons.
    message.AddAIGenerated();
    message.AddFeedback(FeedbackTypes.Default);
    message.WithSuggestedActions(
        new SuggestedActions().AddActions(
            new SuggestedAction(
                ActionTypes.IMBack,
                "Inspect failed runs",
                "/github"),
            new SuggestedAction(
                ActionTypes.IMBack,
                "Create remediation plan",
                "Create a remediation plan for the latest GitHub Actions failures.")));

    for (int index = 0; index < Math.Min(analysis.Runs.Count, 5); index++)
    {
        GitHubWorkflowRun run = analysis.Runs[index].Run;
        message.AddCitation(
            index + 1,
            new CitationAppearance
            {
                Name = $"{run.Name} — run {run.RunNumber}",
                Abstract = $"{run.Conclusion ?? run.Status} on {run.HeadBranch}",
                Url = new Uri(run.HtmlUrl),
            });
    }

    return message;
}

static void DecorateAiResponse(MessageActivity message, string userText)
{
    message.AddAIGenerated();
    message.AddFeedback(FeedbackTypes.Default);
    message.WithSuggestedActions(
        new SuggestedActions().AddActions(
            new SuggestedAction(
                ActionTypes.IMBack,
                "Create rollback plan",
                $"Create a rollback plan for: {userText}"),
            new SuggestedAction(
                ActionTypes.IMBack,
                "Inspect GitHub Actions",
                "/github")));
}

static string? GetCardValue(Dictionary<string, object> data, string key)
{
    if (!data.TryGetValue(key, out object? value) || value is null)
    {
        return null;
    }

    return value is JsonElement element
        ? element.ToString()
        : value.ToString();
}

static string? NormalizeReactionTrigger(string? reactionType)
{
    if (string.IsNullOrWhiteSpace(reactionType))
    {
        return null;
    }

    // Teams may deliver SDK aliases or raw catalog IDs, so both forms are normalized.
    if (reactionType.Equals(ReactionTypes.Like, StringComparison.OrdinalIgnoreCase)
        || reactionType.Equals("like", StringComparison.OrdinalIgnoreCase)
        || reactionType.Contains("thumbsup", StringComparison.OrdinalIgnoreCase))
    {
        return "like";
    }

    if (reactionType.Equals(ReactionTypes.Checkmark, StringComparison.OrdinalIgnoreCase)
        || reactionType.Contains("checkmark", StringComparison.OrdinalIgnoreCase))
    {
        return "checkmark";
    }

    if (reactionType.Equals(ReactionTypes.Pushpin, StringComparison.OrdinalIgnoreCase)
        || reactionType.Contains("pushpin", StringComparison.OrdinalIgnoreCase))
    {
        return "pushpin";
    }

    if (reactionType.Equals(ReactionTypes.Exclamation, StringComparison.OrdinalIgnoreCase)
        || reactionType.Contains("exclamation", StringComparison.OrdinalIgnoreCase))
    {
        return "exclamation";
    }

    return null;
}
