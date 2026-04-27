using System.Text;
using System.Text.Json;

namespace PrReviewBot.src.Services;

public class GitHubService(IHttpClientFactory factory, ILogger<GitHubService> logger)
{
    public async Task<string> GetDiffAsync(string owner, string repo, int prNumber)
    {
        var client = factory.CreateClient("github");
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/repos/{owner}/{repo}/pulls/{prNumber}");
        request.Headers.Add("Accept", "application/vnd.github.diff");

        logger.LogInformation("Fetching diff for {Owner}/{Repo}#{PR}", owner, repo, prNumber);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var diff = await response.Content.ReadAsStringAsync();
        // Truncate to avoid token limits — keep first 30k chars
        return diff.Length > 30_000
            ? diff[..30_000] + "\n\n[diff truncated — showing first 30k chars]"
            : diff;
    }

    public async Task PostCommentAsync(string owner, string repo, int prNumber, string body)
    {
        var client = factory.CreateClient("github");
        var payload = JsonSerializer.Serialize(new { body });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        logger.LogInformation("Posting review comment on {Owner}/{Repo}#{PR}", owner, repo, prNumber);
        var response = await client.PostAsync(
            $"/repos/{owner}/{repo}/issues/{prNumber}/comments", content);
        response.EnsureSuccessStatusCode();
    }
}