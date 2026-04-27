using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrReviewBot.src.Models;

namespace PrReviewBot.src.Services;

public class WebhookService(ILogger<WebhookService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public bool VerifySignature(string payload, string signature, string secret)
    {
        if (string.IsNullOrEmpty(secret)) return true;
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        var expected = "sha256=" + Convert.ToHexString(hash).ToLower();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    public PullRequestEvent? ParsePrEvent(string payload, string githubEvent)
    {
        if (githubEvent != "pull_request") return null;

        try
        {
            var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var action = root.GetProperty("action").GetString();
            if (action is not ("opened" or "synchronize")) return null;

            var pr = root.GetProperty("pull_request");
            var repo = root.GetProperty("repository");

            return new PullRequestEvent(
                action!,
                new PullRequest(
                    pr.GetProperty("number").GetInt32(),
                    pr.GetProperty("title").GetString()!,
                    pr.TryGetProperty("body", out var b) ? b.GetString() : null,
                    pr.GetProperty("html_url").GetString()!
                ),
                new Repository(
                    repo.GetProperty("full_name").GetString()!,
                    new Owner(repo.GetProperty("owner").GetProperty("login").GetString()!),
                    repo.GetProperty("name").GetString()!
                )
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse webhook payload");
            return null;
        }
    }
}