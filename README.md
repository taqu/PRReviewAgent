# PRReviewAgent

PRReviewAgent is an AI-assisted code-review service for GitHub pull requests and GitLab merge requests.

It combines a main review LLM with static structural context and can optionally learn lightweight project-specific review rules from merged changes.

## Features

- AI-assisted pull/merge request review using any OpenAI-compatible API
- GitHub and GitLab integration
- Structural and AST-assisted review context
- Configurable source-file extensions
- Multilingual review output (configurable via templates)
- Optional project-specific rule learning via a dedicated Rule Extraction SubAgent
- Separate lightweight LLM for project adaptation, independent of the main reviewer

## Architecture

```
Pull / Merge Request
        |
        v
Static / Structural Context
        |
        v
Main Review Agent
        |
        v
Main LLM
        |
        v
Code Review


Merged Change (optional project adaptation)
        |
        v
Compact Change Context
        |
        v
Rule Extraction SubAgent
        |
        v
Sub LLM
        |
   +----+----+
   |         |
   v         v
learned    UNKNOWN
  rule        |
   |          v
   v       discard
 store
   |
   v
Learned Project Rules
(used as context in future reviews)
```

## Supported Source Languages

The default configuration covers:

| Language | Extensions                          |
| :------- | :---------------------------------- |
| C        | `.c`                                |
| C++      | `.cpp`, `.cxx`, `.cc`, `.h`, `.hpp`, `.inl` |
| C#       | `.cs`                               |
| Python   | `.py`                               |
| Rust     | `.rs`                               |

Reviewed extensions are configurable via `target_extensions` in `config.toml`.

## Getting Started

### Requirements

- .NET 9.x

### Setup

1. Copy `config.template.toml` to `config.toml` and fill in the required fields.
2. Create `secrets.toml` with your API keys and tokens.

#### Configuration (`config.toml`)

**Common and server:**
```toml
[common]
default_language = "en"
git_provider = "github" # or "gitlab"
warm_up = true

[server]
url = "http://localhost:5000"
log_level = "Warning"
trust_certificate = false
```

**Main Review Agent:**
```toml
[agent]
# Main LLM used for code review.
reviewer = "http://localhost:9090/v1"
reviewer_name = "Reviewer"
reviewer_model = ""
reviewer_temperature = 0.1
reviewer_timeout = 1200
```

**Git providers:**
```toml
[github]
name = "your-github-username"
ssl_verify = true

[gitlab]
url = "https://gitlab.example.com"
ssl_verify = true
```

**File extensions to review:**
```toml
[review]
target_extensions = ["c", "cpp", "cxx", "cc", "h", "hpp", "inl", "cs", "py", "rs"]
```

#### Secrets (`secrets.toml`)

```toml
[openai]
api_key = "your-api-key"

[github]
personal_access_token = "your-token"
shared_secret = "your-webhook-secret"

[gitlab]
personal_access_token = "your-token"
shared_secret = "your-webhook-secret"
```

### Run

```bash
./PRReviewAgent
```

## Usage

### Triggering a Review

Comment `/review` on a pull request or merge request to trigger the agent.

Specify a language with an optional language code:

- `/review` — uses `default_language` from config
- `/review /en` — English
- `/review /ja` — Japanese

## Project Adaptation

Project adaptation is an optional auxiliary feature.

When enabled, merged changes are analyzed to identify small reusable engineering rules or bug-fix patterns specific to the project.

Rule extraction runs through a separate configurable SubAgent backed by its own smaller LLM. This keeps the workload off the main review model and ensures that SubAgent failures cannot affect normal code review.

The extractor uses the changed code as its primary evidence and only a compact subset of relevant structural context. It may intentionally decline to extract a rule when the change does not clearly demonstrate one.

Not every merged change produces a learned rule. Trivial, ambiguous, or insufficiently supported changes are discarded. This behavior is intentional: the system favors precision over recall.

This feature is designed to provide lightweight project-specific context, not to infer a complete coding standard from the repository.

### Enabling Project Adaptation

```toml
[auto_improve]
enabled = true
model_path = "Models/granite-embedding-97M-multilingual-r2-Q8_0.gguf"
db_path = "AppData/review_rules.db"
stale_rule_months = 3
min_confidence_score = 7
context_size = 16384
chunk_overlap = 128

[subagent.rule_extraction]
# Smaller auxiliary LLM for project rule extraction.
enabled = true
endpoint = "http://localhost:9080/v1"
name = "RuleExtractor"
model = ""
max_output = 1024
temperature = 0.0
topp = 0.9
timeout = 120
```

A small local model such as Gemma 4 E4B can be used for rule extraction. Any OpenAI-compatible endpoint is supported.

### LLM Roles

| Role | Description |
| :--- | :---------- |
| Main Review Agent | Reviews code. Should use a capable model with long context and strong reasoning. |
| Rule Extraction SubAgent | Extracts project rules from merged changes. Can use a smaller, faster local model. |

The two roles use independent endpoints and are independently configurable. A Sub LLM failure only disables rule extraction — normal code review continues unaffected.

## Limitations

- Project adaptation does not fine-tune the LLM.
- Not every merged change produces a learned rule.
- Rule extraction intentionally favors conservative results.
- Static and semantic context depth varies by programming language.
- The Rule Extraction SubAgent is optional and does not replace the main reviewer.
