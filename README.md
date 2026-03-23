# PRReviewAgent
An AI-powered Pull Request review agent leveraging Large Language Models (LLMs) to provide automated code reviews and insights.

# Features
- AI-Driven Analysis: Uses OpenAI or OpenAI-compatible APIs for code analysis.
- Language Support: Supports multiple languages for review comments (e.g., English, Japanese) via templates.
- Customizable: Configurable review rules, target file extensions, and AI model parameters.

| Git Provider | Support |
| :--- | :--- |
| GitHub | :white_check_mark: |
| GitLab | :white_check_mark: |

| AI Provider | Support |
| :--- | :--- |
| OpenAI | :white_check_mark: |
| OpenAI Compatible | :white_check_mark: |

# Getting Started
## Setup
1. Copy `PRReviewAgent/secrets.template.toml` to `PRReviewAgent/secrets.toml`.
2. Copy `PRReviewAgent/config.template.toml` to `PRReviewAgent/config.toml`.
3. Fill in the required fields in both files.

### Configuration (`config.toml`)
#### Common & Server
Set the default language and server bindings.
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

#### AI Agents
The system uses three specialized agents. Configure their endpoints and model parameters:
- Assistant: Summarizes diffs and handles simple tasks.
- Planner: Plans reviews and handles logical reasoning.
- Executor: Performs the detailed reviews and organizes the final output.

```toml
[agent]
assistant = "https://api.openai.com/v1"
assistant_model = "gpt-4o-mini"
assistant_temperature = 0.1

planner = "https://api.openai.com/v1"
planner_model = "gpt-4o-mini"
planner_temperature = 0.1

executor = "https://api.openai.com/v1"
executor_model = "gpt-4o"
executor_temperature = 0.1
```

#### Git Providers
**GitHub:**
```toml
[github]
name = "your-github-username"
ssl_verify = true
```

**GitLab:**
```toml
[gitlab]
url = "https://gitlab.com"
ssl_verify = true
```

### Secrets (`secrets.toml`)
**OpenAI:**
```toml
[openai]
api_key = "your-openai-api-key"
```

**GitHub/GitLab:**
```toml
[github]
personal_access_token = "your-token"
shared_secret = "your-webhook-secret"

[gitlab]
personal_access_token = "your-token"
shared_secret = "your-webhook-secret"
```

## Usage
### Triggering a Review
Comment `/review` on a Pull Request or Merge Request to trigger the agent.
You can specify a language by adding a language code:
- `/review` (uses `default_language` from config)
- `/review /en` (English)
- `/review /ja` (Japanese)

### Target Extensions
You can configure which file types the agent should review in `config.toml`:
```toml
[review]
target_extensions = ["cs", "py", "js", "ts", "rs", "cpp"]
```
