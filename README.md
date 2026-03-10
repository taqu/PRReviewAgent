# PRReviewAgent
Pull request review agent by LLM.

# Features

| Git Provider | GitLab |
| :--- | :--- |
| review | :white_check_mark: |

| AI Provider | |
| :--- | :--- |
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
```

Set llm providers,
```
[agent]
planner = "https://api.openai.com/v1"
executor = "https://api.openai.com/v1
```

### GitLab
In `config.toml`,
```
[gitlab]
url = "http://localhost"
ssl_verify = false
```

```
[gitlab]
personal_access_token = "" # Gitlab personal access token
shared_secret = ""  # webhook secret
```

## Review
Comment "/review" on pull request's comments.
Optionally, you may put a language code like "/en".
