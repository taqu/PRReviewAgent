# PRReviewAgent
Pull request review agent by LLM.

# Features

| Git Provider | GitLab |
| :--- | :--- |
| review | :white_check_mark: |

| AI Provider | |
| :--- | :--- |
| OpenAI | :white_check_mark: |
| OpenAI Compatible | :white_check_mark: |

# Getting Started
## Setup
Rename `secrets.template.toml` to `secrets.toml`.
Rename `config.template.toml` to `config.toml`.
Fill `secrets.toml` and `config.toml`.

### Config
Set the agent server bindings in `cofing.toml`.
```
[server]
url = "http://localhost"
log_level = "Warning"
trust_certificate = false # always trust certificate
trusted_certificates = [""]
```

Set llm providers,
```
[agent]
# Used for summarize diffs, simple task.
assistant = "https://api.openai.com/v1"
# Used for planning reviews, slightly complicated logical reasoning task.
planner = "https://api.openai.com/v1"
# Used for each review and organizing reviews, needs long context and understanding long sentences.
executor = "https://api.openai.com/v1
```

For OpenAI in `secrets.toml`,
```
[openai]
api_key = ""
```

### GitLab
In `config.toml`,
```
[gitlab]
url = "http://localhost"
ssl_verify = false
```

In `secrets.toml`,
```
[gitlab]
personal_access_token = "" # Gitlab personal access token
shared_secret = ""  # webhook secret
```

### GitHub
In `config.toml`,
```
[github]
name = "" # repository username
ssl_verify = false
```

In `secrets.toml`,
```
[github]
personal_access_token = "" # GitHub personal access token
shared_secret = ""  # webhook secret
```

## Review
Comment "/review" on pull request's comments.
Optionally, you may put a language code like "/en".
