# PR Review Bot

AI-powered GitHub PR reviewer using a **prompt chaining pipeline** — classifier → parallel checks → synthesizer.

## Architecture

```
PR opened
    │
    ▼
[1] Classifier          — what type of change is this?
    │
    ▼
[2] Parallel checks     — runs simultaneously
    ├── Security check
    ├── Logic check
    └── Quality check
    │
    ▼
[3] Synthesizer         — combines all results into final comment
    │
    ▼
GitHub comment posted
```

## Setup

### 1. Clone & restore

```bash
git clone https://github.com/your-org/pr-review-bot
cd pr-review-bot
dotnet restore
```

### 2. Environment variables

| Variable | Required | Description |
|---|---|---|
| `ANTHROPIC_API_KEY` | ✅ | From console.anthropic.com |
| `GITHUB_TOKEN` | ✅ | PAT with `repo` scope |
| `WEBHOOK_SECRET` | Recommended | Any random string |

### 3. Run locally

```powershell
$env:ANTHROPIC_API_KEY="sk-ant-..."
$env:GITHUB_TOKEN="ghp_..."
$env:WEBHOOK_SECRET="your-secret"
dotnet run
```

### 4. Expose via ngrok (dev only)

```bash
ngrok http 3000
```

### 5. GitHub Webhook

Settings → Webhooks → Add webhook:
- **URL**: `https://your-domain.com/webhook`
- **Content type**: `application/json`
- **Secret**: value of `WEBHOOK_SECRET`
- **Events**: Pull requests only

## Production deploy

### Railway (recommended — simplest)

```bash
railway init
railway up
railway variables set ANTHROPIC_API_KEY=... GITHUB_TOKEN=... WEBHOOK_SECRET=...
```

### Docker

```bash
docker build -t pr-review-bot .
docker run -p 3000:3000 \
  -e ANTHROPIC_API_KEY=... \
  -e GITHUB_TOKEN=... \
  -e WEBHOOK_SECRET=... \
  pr-review-bot
```

## Customizing the prompts

All prompts are in `src/Prompts/ReviewPrompts.cs`. Add your team's rules:

```csharp
public const string QualityReviewer = """
    ...existing prompt...
    
    Additional rules for our team:
    - No magic numbers — use named constants
    - Repository methods must be async
    - Controllers must not contain business logic
    """;
```

## Endpoints

| Endpoint | Description |
|---|---|
| `GET /` | Version info |
| `GET /health` | Health check |
| `POST /webhook` | GitHub webhook receiver |
