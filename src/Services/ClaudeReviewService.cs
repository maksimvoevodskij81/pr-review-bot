using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using PrReviewBot.src.Prompts;
using PrReviewBot.src.Models;

namespace PrReviewBot.src.Services;

public class ClaudeReviewService(AnthropicClient claude, ILogger<ClaudeReviewService> logger)
{
    private const string Model = "claude-sonnet-4-5";

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<PrReviewResult> ReviewAsync(
        string title, string? description, string diff)
    {
        var diffMessage = ReviewPrompts.BuildDiffMessage(title, description, diff);

        // Step 1: classify (fast, cheap — sets context for everything else)
        logger.LogInformation("Step 1: Classifying PR");
        var classification = await ClassifyAsync(diffMessage);

        // Step 2: run three reviewers in parallel
        logger.LogInformation("Step 2: Running parallel checks");
        var (security, logic, quality) = await RunParallelChecksAsync(diffMessage);

        // Step 3: synthesize into final comment
        logger.LogInformation("Step 3: Synthesizing review");
        var synthesisInput = ReviewPrompts.BuildSynthesisMessage(
            classification.ChangeType.ToString(),
            classification.Summary,
            security.Issues ?? "No issues found",
            logic.Issues ?? "No issues found",
            quality.Issues ?? "No issues found"
        );
        var finalComment = await SynthesizeAsync(synthesisInput);
        var verdict = ExtractVerdict(finalComment);

        return new PrReviewResult(
            classification,
            [security, logic, quality],
            verdict,
            FormatComment(finalComment)
        );
    }

    // ── Pipeline steps ────────────────────────────────────────────────────────

    private async Task<ClassificationResult> ClassifyAsync(string diffMessage)
    {
        var response = await CallClaudeAsync(ReviewPrompts.Classifier, diffMessage, maxTokens: 200);
        try
        {
            // Strip markdown fences if model wraps response
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                response, @"```json|```", "").Trim();
            var json = JsonSerializer.Deserialize<JsonElement>(cleaned);
            var changeType = Enum.TryParse<ChangeType>(
                json.GetProperty("changeType").GetString(), out var ct) ? ct : ChangeType.Unknown;
            var summary = json.GetProperty("summary").GetString() ?? "";
            return new ClassificationResult(changeType, summary);
        }
        catch
        {
            logger.LogWarning("Failed to parse classification JSON, using defaults");
            return new ClassificationResult(ChangeType.Unknown, "Unable to classify");
        }
    }

    private async Task<(ReviewCheckResult Security, ReviewCheckResult Logic, ReviewCheckResult Quality)>
        RunParallelChecksAsync(string diffMessage)
    {
        // All three run simultaneously — saves ~2/3 of latency vs sequential
        var securityTask = RunCheckAsync("Security", ReviewPrompts.SecurityReviewer, diffMessage);
        var logicTask = RunCheckAsync("Logic", ReviewPrompts.LogicReviewer, diffMessage);
        var qualityTask = RunCheckAsync("Quality", ReviewPrompts.QualityReviewer, diffMessage);

        await Task.WhenAll(securityTask, logicTask, qualityTask);
        return (await securityTask, await logicTask, await qualityTask);
    }

    private async Task<ReviewCheckResult> RunCheckAsync(
        string category, string systemPrompt, string diffMessage)
    {
        var response = await CallClaudeAsync(systemPrompt, diffMessage, maxTokens: 400);
        try
        {
            var cleaned = System.Text.RegularExpressions.Regex.Replace(response, @"```json|```", "").Trim();
            var json = JsonSerializer.Deserialize<JsonElement>(cleaned);
            var hasIssues = json.GetProperty("hasIssues").GetBoolean();
            var issues = json.TryGetProperty("issues", out var i) ? i.GetString() : null;
            return new ReviewCheckResult(category, issues, hasIssues);
        }
        catch
        {
            logger.LogWarning("Failed to parse {Category} check JSON", category);
            return new ReviewCheckResult(category, null, false);
        }
    }

    private async Task<string> SynthesizeAsync(string synthesisInput)
    {
        return await CallClaudeAsync(ReviewPrompts.Synthesizer, synthesisInput, maxTokens: 600);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> CallClaudeAsync(
        string systemPrompt, string userMessage, int maxTokens)
    {
        var response = await claude.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = Model,
            MaxTokens = maxTokens,
            SystemMessage = systemPrompt,
            Messages =
            [
                new Message
                {
                    Role = RoleType.User,
                    Content = [new TextContent { Text = userMessage }]
                }
            ]
        });
        return response.Content[0].ToString()!.Trim();
    }

    private static string ExtractVerdict(string comment)
    {
        if (comment.Contains("❌")) return "❌ Needs work";
        if (comment.Contains("⚠️")) return "⚠️ Minor issues";
        return "✅ LGTM";
    }

    private static string FormatComment(string review) =>
        $"""
        ## 🤖 AI Code Review

        {review}

        ---
        *Automated review by Claude · [What is this?](https://github.com/maksimvoevodskij81/pr-review-bot)*
        """;
}