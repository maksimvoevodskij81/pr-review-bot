namespace PrReviewBot.src.Models;

public record PullRequestEvent(
    string Action,
    PullRequest PullRequest,
    Repository Repository
);

public record PullRequest(
    int Number,
    string Title,
    string? Body,
    string HtmlUrl
);

public record Repository(
    string FullName,
    Owner Owner,
    string Name
);

public record Owner(string Login);

// ── Review pipeline models ────────────────────────────────────────────────────

public enum ChangeType { Feature, Refactor, BugFix, Config, Test, Unknown }

public record ClassificationResult(ChangeType ChangeType, string Summary);

public record ReviewCheckResult(string Category, string? Issues, bool HasIssues);

public record PrReviewResult(
    ClassificationResult Classification,
    IReadOnlyList<ReviewCheckResult> Checks,
    string Verdict,        // ✅ LGTM / ⚠️ Minor issues / ❌ Needs work
    string FormattedComment
);