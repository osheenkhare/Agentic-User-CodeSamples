using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

internal sealed class GitHubActionsService
{
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, GitHubActionsAnalysis> latestByConversation = new();

    public GitHubActionsService(HttpClient httpClient, string token)
    {
        this.httpClient = httpClient;
        this.httpClient.BaseAddress = new Uri("https://api.github.com/");
        this.httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("teams-sdk-capabilities/1.0");
        this.httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        this.httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public bool TryGetLatest(string conversationId, out GitHubActionsAnalysis? analysis) =>
        latestByConversation.TryGetValue(conversationId, out analysis);

    public async Task<GitHubActionsAnalysis> AnalyzeAsync(
        string conversationId,
        string repository,
        string? branch,
        string status,
        int lookbackDays,
        CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-lookbackDays);
        string statusQuery = status.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"&status={Uri.EscapeDataString(status)}";

        using HttpResponseMessage response = await httpClient.GetAsync(
            $"repos/{repository}/actions/runs?per_page=30{statusQuery}",
            cancellationToken);
        EnsureGitHubSuccess(response, repository);

        GitHubWorkflowRunsResponse payload =
            await response.Content.ReadFromJsonAsync<GitHubWorkflowRunsResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "GitHub returned an empty workflow-runs response.");

        List<GitHubWorkflowRun> runs =
        [
            .. payload.WorkflowRuns
                .Where(run => run.CreatedAt >= cutoff)
                .Where(run =>
                    string.IsNullOrWhiteSpace(branch)
                    || run.HeadBranch.Equals(branch, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(run => run.CreatedAt)
                .Take(10),
        ];

        List<GitHubRunFinding> findings = [];
        foreach (GitHubWorkflowRun run in runs)
        {
            IReadOnlyList<string> failedSteps =
                run.Conclusion is "failure" or "cancelled" or "timed_out"
                    ? await GetFailedStepsAsync(repository, run.Id, cancellationToken)
                    : [];
            findings.Add(new GitHubRunFinding(run, failedSteps));
        }

        GitHubActionsAnalysis analysis = new(
            repository,
            branch,
            status,
            lookbackDays,
            DateTimeOffset.UtcNow,
            findings);

        latestByConversation[conversationId] = analysis;
        return analysis;
    }

    private async Task<IReadOnlyList<string>> GetFailedStepsAsync(
        string repository,
        long runId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"repos/{repository}/actions/runs/{runId}/jobs?filter=latest&per_page=100",
            cancellationToken);
        EnsureGitHubSuccess(response, repository);

        GitHubJobsResponse payload =
            await response.Content.ReadFromJsonAsync<GitHubJobsResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("GitHub returned an empty jobs response.");

        return payload.Jobs
            .SelectMany(job => job.Steps
                .Where(step => step.Conclusion == "failure")
                .Select(step => $"{job.Name}: {step.Name}"))
            .Take(10)
            .ToArray();
    }

    private static void EnsureGitHubSuccess(
        HttpResponseMessage response,
        string repository)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string reason = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "GitHub rejected the configured token.",
            System.Net.HttpStatusCode.Forbidden =>
                "GitHub denied access or rate-limited the request.",
            System.Net.HttpStatusCode.NotFound =>
                $"Repository `{repository}` was not found or is not accessible.",
            _ => $"GitHub returned HTTP {(int)response.StatusCode}.",
        };

        throw new InvalidOperationException(reason);
    }

    private sealed record GitHubWorkflowRunsResponse(
        [property: JsonPropertyName("workflow_runs")]
        IReadOnlyList<GitHubWorkflowRun> WorkflowRuns);

    private sealed record GitHubJobsResponse(
        [property: JsonPropertyName("jobs")] IReadOnlyList<GitHubJob> Jobs);

    private sealed record GitHubJob(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("steps")] IReadOnlyList<GitHubJobStep> Steps);

    private sealed record GitHubJobStep(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("conclusion")] string? Conclusion);
}

internal sealed record GitHubActionsAnalysis(
    string Repository,
    string? Branch,
    string RequestedStatus,
    int LookbackDays,
    DateTimeOffset CompletedAt,
    IReadOnlyList<GitHubRunFinding> Runs)
{
    public string ToNotification()
    {
        int failed = Runs.Count(finding =>
            finding.Run.Conclusion is "failure" or "cancelled" or "timed_out");
        return Runs.Count == 0
            ? $"GitHub Actions analysis completed for `{Repository}`. No matching runs were found in the last {LookbackDays} day(s)."
            : $"GitHub Actions analysis completed for `{Repository}`: {Runs.Count} matching run(s), {failed} requiring attention. Ask `show latest GitHub Actions analysis` for the cited report.";
    }

    public string ToReport()
    {
        if (Runs.Count == 0)
        {
            return $"No `{RequestedStatus}` workflow runs were found for `{Repository}` in the last {LookbackDays} day(s).";
        }

        int failed = Runs.Count(finding =>
            finding.Run.Conclusion is "failure" or "cancelled" or "timed_out");
        List<string> lines =
        [
            $"GitHub Actions report for `{Repository}`:",
            "",
            $"- Reviewed: **{Runs.Count}** recent run(s)",
            $"- Requiring attention: **{failed}**",
            "",
        ];

        for (int index = 0; index < Math.Min(Runs.Count, 5); index++)
        {
            GitHubRunFinding finding = Runs[index];
            GitHubWorkflowRun run = finding.Run;
            lines.Add(
                $"{index + 1}. **{run.Name}** — `{run.Conclusion ?? run.Status}` on `{run.HeadBranch}` ([run {run.RunNumber}][{index + 1}])");
            foreach (string failedStep in finding.FailedSteps.Take(3))
            {
                lines.Add($"   - Failed step: `{failedStep}`");
            }
        }

        if (failed > 0)
        {
            lines.Add("");
            lines.Add("Recommended next step: open the newest failed run, inspect its first failing job, and compare it with the most recent successful run on the same branch.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record GitHubRunFinding(
    GitHubWorkflowRun Run,
    IReadOnlyList<string> FailedSteps);

internal sealed record GitHubWorkflowRun(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("run_number")] long RunNumber,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("head_branch")] string HeadBranch,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
