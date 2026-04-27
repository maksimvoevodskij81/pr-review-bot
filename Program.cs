using Anthropic.SDK;
using PrReviewBot.src.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
var anthropicKey = Env("ANTHROPIC_API_KEY");
var githubToken = Env("GITHUB_TOKEN");
var webhookSecret = Environment.GetEnvironmentVariable("WEBHOOK_SECRET") ?? "";

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(new AnthropicClient(anthropicKey));
builder.Services.AddSingleton<ClaudeReviewService>();
builder.Services.AddSingleton<WebhookService>();
builder.Services.AddSingleton<GitHubService>();

builder.Services.AddHttpClient("github", c =>
{
    c.BaseAddress = new Uri("https://api.github.com");
    c.DefaultRequestHeaders.Add("Authorization", $"Bearer {githubToken}");
    c.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    c.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    c.DefaultRequestHeaders.Add("User-Agent", "PrReviewBot/2.0");
});

builder.Logging.AddConsole();

var app = builder.Build();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new { status = "ok", version = "2.0" }));

app.MapGet("/health", () => Results.Ok(new { healthy = true }));

app.MapPost("/webhook", async (
    HttpRequest request,
    WebhookService webhook,
    GitHubService github,
    ClaudeReviewService reviewer,
    ILogger<Program> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var rawBody = await reader.ReadToEndAsync();

    // Verify GitHub signature
    var sig = request.Headers["X-Hub-Signature-256"].ToString();
    if (!webhook.VerifySignature(rawBody, sig, webhookSecret))
    {
        logger.LogWarning("Invalid webhook signature — rejected");
        return Results.Unauthorized();
    }

    var githubEvent = request.Headers["X-GitHub-Event"].ToString();
    var prEvent = webhook.ParsePrEvent(rawBody, githubEvent);

    if (prEvent is null)
        return Results.Ok("ignored");

    var pr = prEvent.PullRequest;
    var repo = prEvent.Repository;

    logger.LogInformation("PR #{Number} opened in {Repo} — starting review",
        pr.Number, repo.FullName);

    // Fire and forget — respond to GitHub immediately (it has a 10s timeout)
    _ = Task.Run(async () =>
    {
        try
        {
            var diff = await github.GetDiffAsync(repo.Owner.Login, repo.Name, pr.Number);
            var result = await reviewer.ReviewAsync(pr.Title, pr.Body, diff);

            await github.PostCommentAsync(
                repo.Owner.Login, repo.Name, pr.Number, result.FormattedComment);

            logger.LogInformation("✅ Review posted on PR #{Number} — {Verdict}",
                pr.Number, result.Verdict);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Review failed for PR #{Number}", pr.Number);
        }
    });

    return Results.Ok("accepted");
});

app.Run();

static string Env(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Missing required env var: {name}");
