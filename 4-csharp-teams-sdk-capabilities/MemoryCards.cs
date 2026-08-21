using System.Text.Json;
using Microsoft.Teams.Apps.Schema;

internal static class MemoryCards
{
    public static MessageActivity CreateIntroMessage(string memoryActionScopeId) =>
        CreateMessage(JsonSerializer.SerializeToElement(new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                Header("Memory boundary"),
                Text(
                    "Conversation history is isolated automatically. These controls optionally create and inspect an explicit demo fact."),
                Facts(
                [
                    new { title = "Natural test", value = "My project codename is Bluebird. Keep that in mind." },
                    new { title = "Inspect", value = "/memory" },
                    new { title = "Clear", value = "/forget" },
                ]),
            },
            actions = new object[]
            {
                new
                {
                    type = "Action.Execute",
                    title = "Save Bluebird in this scope",
                    verb = "memory_demo_save",
                    style = "positive",
                    data = new
                    {
                        action = "memory_demo_save",
                        memoryActionScopeId,
                    },
                },
                new
                {
                    type = "Action.Execute",
                    title = "Inspect current memory",
                    verb = "memory_demo_inspect",
                    data = new
                    {
                        action = "memory_demo_inspect",
                        memoryActionScopeId,
                    },
                },
            },
        }));

    public static MessageActivity CreateSavedMessage(
        ConversationMemoryScope scope,
        MemoryFact fact) =>
        CreateMessage(CreateSavedCard(scope, fact));

    public static JsonElement CreateSavedCard(
        ConversationMemoryScope scope,
        MemoryFact fact) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                Header("Memory saved", "Good"),
                Facts(
                [
                    new { title = "Scope", value = scope.Label },
                    new { title = "Scope ID", value = scope.DisplayId },
                    new { title = "Expires", value = fact.ExpiresAt.ToString("u") },
                ]),
                Text(fact.Text, monospace: true),
                Text(
                    "This fact is available only inside the scope shown above.",
                    subtle: true),
            },
        });

    public static MessageActivity CreateStatusMessage(
        ConversationMemoryScope scope,
        IReadOnlyList<MemoryFact> facts) =>
        CreateMessage(CreateStatusCard(scope, facts));

    public static JsonElement CreateStatusCard(
        ConversationMemoryScope scope,
        IReadOnlyList<MemoryFact> facts)
    {
        List<object> body =
        [
            Header("Current memory scope"),
            Facts(
            [
                new { title = "Scope", value = scope.Label },
                new { title = "Scope ID", value = scope.DisplayId },
                new { title = "Stored items", value = facts.Count.ToString() },
                new
                {
                    title = "Next expiration",
                    value = facts.Count == 0
                        ? "None"
                        : facts.Min(fact => fact.ExpiresAt).ToString("u"),
                },
            ]),
        ];
        if (facts.Count == 0)
        {
            body.Add(Text("No explicit memory is stored in this scope.", subtle: true));
        }
        else
        {
            body.Add(Text("**Memory in this scope**"));
            foreach (MemoryFact fact in facts)
            {
                body.Add(Text($"- {fact.Text}"));
            }
        }
        body.Add(
            Text(
                "The application does not query or enumerate any other scope.",
                subtle: true));

        return JsonSerializer.SerializeToElement(new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body,
        });
    }

    public static MessageActivity CreateForgetConfirmationMessage(
        ConversationMemoryScope scope,
        int itemCount,
        string memoryActionScopeId) =>
        CreateMessage(JsonSerializer.SerializeToElement(new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                Header("Clear current memory?"),
                Text(
                    $"This clears {itemCount} explicit item(s) and conversation history from **{scope.Label}** only."),
                Text(
                    "Memory in every other 1:1, group chat, channel, and thread remains untouched.",
                    subtle: true),
            },
            actions = new object[]
            {
                new
                {
                    type = "Action.Execute",
                    title = "Clear this scope",
                    verb = "memory_forget_confirm",
                    style = "destructive",
                    data = new
                    {
                        action = "memory_forget_confirm",
                        memoryActionScopeId,
                    },
                },
                new
                {
                    type = "Action.Execute",
                    title = "Cancel",
                    verb = "memory_forget_cancel",
                    data = new
                    {
                        action = "memory_forget_cancel",
                        memoryActionScopeId,
                    },
                },
            },
        }));

    public static JsonElement CreateClearedCard(
        ConversationMemoryScope scope,
        int removed) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                Header("Memory cleared", "Good"),
                Text(
                    $"Removed {removed} explicit item(s) and reset conversation history for **{scope.Label}**."),
                Text("No other memory scope was changed.", subtle: true),
            },
        });

    public static JsonElement CreateCancelledCard(ConversationMemoryScope scope) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                Header("Memory unchanged"),
                Text($"Nothing was cleared from **{scope.Label}**."),
            },
        });

    private static object Header(string value, string color = "Accent") => new
    {
        type = "TextBlock",
        text = value,
        weight = "Bolder",
        size = "Medium",
        color,
        wrap = true,
    };

    private static object Text(
        string value,
        bool subtle = false,
        bool monospace = false) => new
        {
            type = "TextBlock",
            text = value,
            wrap = true,
            isSubtle = subtle,
            fontType = monospace ? "Monospace" : "Default",
            spacing = "Small",
        };

    private static object Facts(object[] facts) => new
    {
        type = "Container",
        style = "emphasis",
        spacing = "Medium",
        items = new object[]
        {
            new
            {
                type = "FactSet",
                facts,
            },
        },
    };

    private static MessageActivity CreateMessage(JsonElement card)
    {
        TeamsAttachment attachment = TeamsAttachment.CreateBuilder()
            .WithAdaptiveCard(card)
            .Build();
        MessageActivity message = new();
        message.AddAttachment(attachment);
        return message;
    }
}
