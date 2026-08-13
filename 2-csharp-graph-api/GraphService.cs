using Azure.Identity;
using Microsoft.Graph;

namespace GraphApiStreamingApp;

internal sealed class GraphService
{
    private static readonly string[] Scopes =
        ["https://graph.microsoft.com/.default"];

    private readonly GraphServiceClient _graphClient;

    private GraphService(GraphServiceClient graphClient) =>
        _graphClient = graphClient;

    internal static GraphService Create(IConfiguration configuration)
    {
        string tenantId = Required(configuration, "Graph:TenantId");
        string clientId = Required(configuration, "Graph:ClientId");
        string clientSecret = Required(configuration, "Graph:ClientSecret");

        ClientSecretCredential credential = new(
            tenantId,
            clientId,
            clientSecret);

        return new GraphService(new GraphServiceClient(credential, Scopes));
    }

    internal async Task<UserProfile> GetUserProfileAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new UserProfile("there", "your inbox");
        }

        var user = await _graphClient.Users[userId].GetAsync(
            request => request.QueryParameters.Select =
                ["displayName", "mail", "userPrincipalName"],
            cancellationToken);

        string displayName = user?.DisplayName
            ?? user?.UserPrincipalName
            ?? "there";
        string email = user?.Mail
            ?? user?.UserPrincipalName
            ?? "your inbox";

        return new UserProfile(displayName, email);
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Missing required configuration value: {key}");
}

internal sealed record UserProfile(string DisplayName, string Email);
