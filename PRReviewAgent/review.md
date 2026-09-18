# Implementation Instructions — Per-User Auto Review Opt-In/Opt-Out

## Objective

Add a provider-neutral automatic review preference system that allows each user to enable or disable automatic review per project.

The feature must work consistently for both GitHub pull requests and GitLab merge requests.

The system must support:

```text
/auto_review on
/auto_review on /ja
/auto_review on /en
/auto_review off
```

and reuse the existing review pipeline already triggered by:

```text
/review
/review /ja
/review /en
```

The main outcome must be:

> Automatic review behavior is controlled by server configuration plus a persistent per-project/per-user override stored in SQLite, while all actual reviews continue to use the existing review execution sequence.

---

# 1. Existing Review Command Behavior

The application already supports:

```text
/review
/review /ja
/review /en
```

The configured default language comes from:

```toml
[common]
default_language = "ja"
```

The new auto-review commands must reuse the same language parsing and review execution logic.

Do not build a second review pipeline specifically for automatic review.

---

# 2. New Commands

Support:

```text
/auto_review on
/auto_review on /ja
/auto_review on /en
/auto_review off
```

Semantics:

## `/auto_review on`

* Persist automatic review as enabled for the current `(ProjectId, UserId)`.
* Resolve language using `config.toml` `default_language`.
* Immediately start the normal review sequence for the current PR/MR.

## `/auto_review on /ja`

* Persist automatic review as enabled.
* Start the normal review sequence immediately.
* Use Japanese for this review.

## `/auto_review on /en`

* Persist automatic review as enabled.
* Start the normal review sequence immediately.
* Use English for this review.

## `/auto_review off`

* Persist automatic review as disabled.
* Do not start a review.
* Return a confirmation response.

---

# 3. Language Scope

The language argument on:

```text
/auto_review on /ja
/auto_review on /en
```

applies only to the review triggered by that command.

Do not persist a user-specific language preference in SQLite.

Future automatic reviews triggered by a newly opened PR/MR must use:

```text
[common].default_language
```

from `config.toml`.

The persistence model should therefore remain:

```text
ProjectId
UserId
Enabled
```

and should not add:

```text
PreferredLanguage
```

in this phase.

---

# 4. Shared Language Resolution

Reuse the same language parser and validation logic used by `/review`.

Preferred behavior:

```text
command language specified
    -> use command language

command language missing
    -> use config.toml default_language
```

Conceptually:

```csharp
string language =
    command.Language
    ?? _commonOptions.DefaultLanguage;
```

Do not implement separate language parsing for `/auto_review`.

Unknown or unsupported language arguments must behave consistently with `/review`.

---

# 5. Configuration

Add a new section to `config.toml`.

Recommended:

```toml
[auto_review]
enabled = true
mode = "opt_in"
```

Supported modes:

```text
opt_in
opt_out
```

---

# 6. Meaning of `enabled`

`enabled` is the global master switch for automatic review.

## `enabled = false`

Automatic review is completely disabled.

Required behavior:

* PR/MR opened events do not trigger review.
* `/auto_review on` does not enable automatic review.
* `/auto_review off` does not need to modify behavior.
* The command should return a clear message that automatic review is disabled by server configuration.
* Manual `/review` must continue to work normally.

## `enabled = true`

Per-user auto-review configuration is active.

---

# 7. Meaning of `mode`

`mode` defines the default state for users without a stored preference.

## `mode = "opt_in"`

Default:

```text
automatic review OFF
```

Only users who explicitly use:

```text
/auto_review on
```

receive automatic reviews.

## `mode = "opt_out"`

Default:

```text
automatic review ON
```

Users receive automatic reviews unless they explicitly use:

```text
/auto_review off
```

---

# 8. Configuration vs User Override

Use this precedence:

```text
1. auto_review.enabled
2. stored per-user setting
3. auto_review.mode default
```

Conceptually:

```csharp
if (!options.Enabled)
    return false;

bool? userSetting =
    await repository.GetAsync(
        projectId,
        userId,
        cancellationToken);

if (userSetting.HasValue)
    return userSetting.Value;

return options.Mode switch
{
    AutoReviewMode.OptIn => false,
    AutoReviewMode.OptOut => true,
    _ => false
};
```

This distinction is important.

`config.toml` defines the default policy.

SQLite stores explicit user choice.

---

# 9. Configuration Changes Must Preserve Explicit User Choice

Example:

Initial configuration:

```toml
[auto_review]
enabled = true
mode = "opt_in"
```

User A has never configured auto review:

```text
effective state = OFF
```

User B executes:

```text
/auto_review on
```

SQLite stores:

```text
User B = ON
```

Later the administrator changes:

```toml
mode = "opt_out"
```

Expected:

```text
User A -> ON
User B -> ON
```

If User C previously executed:

```text
/auto_review off
```

then:

```text
User C -> OFF
```

Explicit DB state always overrides the default mode.

---

# 10. SQLite Persistence

Add a dedicated table.

Recommended name:

```text
auto_review_user_settings
```

Recommended schema:

```sql
CREATE TABLE auto_review_user_settings (
    project_id      INTEGER NOT NULL,
    user_id         TEXT NOT NULL,
    enabled         INTEGER NOT NULL,

    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,

    PRIMARY KEY(project_id, user_id),

    FOREIGN KEY(project_id)
        REFERENCES projects(id)
);
```

Follow existing naming and timestamp conventions.

---

# 11. Why the Composite Key Is Required

User preference is project-specific.

The same user may want:

```text
Project A -> auto review ON
Project B -> auto review OFF
```

Therefore the logical key must be:

```text
(ProjectId, UserId)
```

Do not key preferences by UserId alone.

---

# 12. Stable User Identity

Use the stable provider user ID supplied by GitHub or GitLab.

Do not use:

* Display name
* Username if a more stable numeric/provider ID exists
* Email address
* Comment author text

The provider adapter must expose a stable user identifier.

Normalize it into the common application model as a string if needed.

---

# 13. GitHub/GitLab Neutral Event Model

Auto-review logic must not depend on GitHub- or GitLab-specific event payload types.

Normalize opened PR/MR events into a common representation.

Example:

```csharp
public sealed record MergeRequestOpenedEvent(
    string ExternalProjectId,
    string MergeRequestId,
    string AuthorUserId);
```

The actual type/name may follow existing conventions.

The important fields are:

```text
Project identity
Merge/Pull Request identity
Author User ID
```

---

# 14. Repository API

Introduce a focused persistence abstraction.

Example:

```csharp
public interface IAutoReviewUserSettingRepository
{
    Task<bool?> GetAsync(
        long projectId,
        string userId,
        CancellationToken cancellationToken);

    Task SetAsync(
        long projectId,
        string userId,
        bool enabled,
        CancellationToken cancellationToken);
}
```

Returning `bool?` is useful:

```text
true  -> explicit ON
false -> explicit OFF
null  -> no user override exists
```

Do not collapse `null` into the config default inside the repository.

The policy layer owns that decision.

---

# 15. Upsert Behavior

Use an atomic SQLite upsert.

Conceptually:

```sql
INSERT INTO auto_review_user_settings (
    project_id,
    user_id,
    enabled,
    created_at,
    updated_at
)
VALUES (
    @projectId,
    @userId,
    @enabled,
    @now,
    @now
)
ON CONFLICT(project_id, user_id)
DO UPDATE SET
    enabled = excluded.enabled,
    updated_at = excluded.updated_at;
```

Preserve `created_at` according to existing conventions.

---

# 16. Add AutoReview Options

Introduce typed configuration.

Example:

```csharp
public sealed class AutoReviewOptions
{
    public bool Enabled { get; init; }

    public AutoReviewMode Mode { get; init; }
}
```

Example enum:

```csharp
public enum AutoReviewMode
{
    OptIn,
    OptOut
}
```

Validate configuration at startup if the application already uses options validation.

Unknown values must not silently become an unsafe default.

Prefer failing configuration validation or explicitly defaulting to `OptIn` according to existing conventions.

---

# 17. Add AutoReview Policy

Introduce a provider-neutral policy service.

Example:

```csharp
public interface IAutoReviewPolicy
{
    Task<bool> IsEnabledAsync(
        long projectId,
        string userId,
        CancellationToken cancellationToken);
}
```

Responsibilities:

```text
global feature switch
+
user override
+
opt-in/opt-out default
```

Do not put this decision logic inside GitHub/GitLab event handlers.

---

# 18. Command Parsing

Extend the existing command parser rather than creating an unrelated parser.

A suitable internal model may be:

```csharp
public enum ReviewCommandType
{
    Review,
    AutoReviewOn,
    AutoReviewOff
}
```

with:

```csharp
public sealed record ReviewCommand(
    ReviewCommandType Type,
    string? Language);
```

Expected parse results:

```text
/review
    -> Review, null

/review /ja
    -> Review, ja

/review /en
    -> Review, en

/auto_review on
    -> AutoReviewOn, null

/auto_review on /ja
    -> AutoReviewOn, ja

/auto_review on /en
    -> AutoReviewOn, en

/auto_review off
    -> AutoReviewOff, null
```

Adapt this to the actual command architecture.

---

# 19. Do Not Duplicate Existing Review Command Logic

If `/review` parsing currently has reusable components for:

```text
command recognition
language validation
language normalization
review invocation
```

reuse them.

Do not create a parallel implementation for `/auto_review on`.

---

# 20. `/auto_review on` Command Flow

Required flow:

```text
Receive comment command
      |
      v
Parse /auto_review on [language]
      |
      v
Check auto_review.enabled
      |
      +-- false
      |      |
      |      v
      |  return feature-disabled message
      |
      +-- true
             |
             v
Resolve Project
             |
             v
Resolve command author User ID
             |
             v
Persist Enabled = true
             |
             v
Resolve review language
             |
             v
Enter existing normal review sequence
```

The actual review must be the same sequence used by `/review`.

---

# 21. `/auto_review off` Command Flow

Required flow:

```text
Receive /auto_review off
      |
      v
Check auto_review.enabled
      |
      +-- disabled
      |      |
      |      v
      |  return feature-disabled message
      |
      +-- enabled
             |
             v
Resolve Project
             |
             v
Resolve command author User ID
             |
             v
Persist Enabled = false
             |
             v
Return confirmation
```

Do not start a review.

---

# 22. Manual `/review` Remains Independent

A user with automatic review disabled must still be able to run:

```text
/review
/review /ja
/review /en
```

Manual review does not consult auto-review preference.

Conceptually:

```text
auto_review preference
    -> controls only automatic triggering

/review
    -> explicit user request
    -> always uses normal review pipeline
```

Do not couple the two.

---

# 23. PR/MR Open Event Flow

When a new pull request or merge request is created:

```text
PR/MR Opened
      |
      v
Resolve Project
      |
      v
Get author User ID
      |
      v
IAutoReviewPolicy.IsEnabledAsync(...)
      |
   +--+--+
   |     |
 false  true
   |     |
   v     v
 stop   Resolve default language
             |
             v
       Existing review pipeline
```

Do not create a dedicated "auto review pipeline."

---

# 24. Which User Controls Open-Event Auto Review

Use the PR/MR author's user ID.

Do not use:

* Webhook sender if different from the author
* Reviewer
* Assignee
* Person who last edited the MR
* Repository owner

The auto-review preference belongs to the author whose newly opened PR/MR is being evaluated.

---

# 25. Automatic Review Language

For PR/MR opened events, use:

```text
[common].default_language
```

because there is no command language argument.

Do not read a persisted per-user language preference because this phase does not store one.

---

# 26. `/auto_review on /ja` Does Two Things

This command is intentionally both:

```text
settings command
+
review trigger
```

Required semantics:

```text
1. Store AutoReview = ON
2. Resolve current review language = ja
3. Execute normal review
```

Do not require the user to then separately enter:

```text
/review /ja
```

---

# 27. `/auto_review on` Without Language

This command must:

```text
1. Store AutoReview = ON
2. Resolve current review language from config.toml
3. Execute normal review
```

Equivalent review-language behavior to:

```text
/review
```

---

# 28. `/auto_review off /ja`

Do not support language arguments for `off` unless there is an existing generic parser reason to accept and ignore them.

Preferred valid syntax:

```text
/auto_review off
```

Preferred invalid syntax:

```text
/auto_review off /ja
```

Handle invalid syntax consistently with the current command parser.

---

# 29. Command Confirmation Behavior

For `/auto_review on`, because the command immediately starts a normal review, avoid creating unnecessary extra persistent comments if the current review-status-comment system already shows:

```text
Review in progress
```

The stored review-status comment can serve as the visible confirmation that the command was accepted.

If command acknowledgment already exists elsewhere, keep it concise.

For `/auto_review off`, return a clear confirmation such as:

```text
Automatic review disabled for this project.
```

Follow the application's existing response style.

---

# 30. Global Feature Disabled Response

If:

```toml
[auto_review]
enabled = false
```

then:

```text
/auto_review on
/auto_review off
```

should not silently succeed.

Return a message equivalent to:

```text
Automatic review is disabled by server configuration.
```

Manual `/review` remains available.

---

# 31. Interaction with Persistent Review Status Comment

The recently introduced owned review-status-comment mechanism must be reused.

When `/auto_review on` triggers a review:

```text
AutoReview setting saved
      |
      v
normal review starts
      |
      v
Ensure owned status comment
      |
      v
Reviewing...
      |
      v
Detection
      |
      v
Selection
      |
      v
Update same comment with result
```

The same applies to a PR/MR opened event.

Do not create a separate auto-review comment type.

---

# 32. Repeated Commands

If a user repeatedly executes:

```text
/auto_review on
```

the state remains:

```text
Enabled = true
```

and each valid command may trigger the normal review sequence again.

The existing per-MR owned comment should be reused.

Similarly:

```text
/auto_review off
```

should be idempotent.

---

# 33. Auto Review Open Event Must Not Duplicate an Already-Running Review

Inspect the existing per-MR review concurrency behavior.

A PR/MR open event and a user command could occur close together.

Example:

```text
MR opened -> auto review starts

immediately afterward:
/auto_review on
```

or:

```text
/review
```

Reuse the existing per-MR review lock or duplicate-execution prevention mechanism.

Do not create a separate global lock.

The automatic trigger should enter the same concurrency boundary as manual review.

---

# 34. Provider-Neutral User Preference

The preference system must remain common across GitHub and GitLab.

Provider adapters are responsible only for extracting:

```text
project ID
MR/PR ID
user ID
event type
command text
```

The following must remain provider-neutral:

```text
AutoReviewOptions
IAutoReviewUserSettingRepository
IAutoReviewPolicy
command semantics
review trigger logic
```

---

# 35. Do Not Share Preferences Across Providers Accidentally

Because `project_id` is already provider/project-specific, the composite key:

```text
(ProjectId, UserId)
```

is sufficient if internal project identity is globally unique.

Do not assume that GitHub user ID `123` and GitLab user ID `123` represent the same human.

Their settings remain isolated because they belong to different project records/provider contexts.

---

# 36. Migration

Add a migration that:

1. Creates `auto_review_user_settings`.
2. Adds the project foreign key.
3. Adds the composite primary key.
4. Adds any genuinely necessary index not already covered by the primary key.

Do not backfill user settings.

Existing users should have:

```text
no explicit DB override
```

and therefore inherit:

```text
auto_review.mode
```

---

# 37. Configuration Template

Update `config.template.toml` with:

```toml
[auto_review]
# Enables automatic review on newly opened pull/merge requests.
enabled = false

# Default policy for users without an explicit per-project preference.
# "opt_in": automatic review is disabled until the user runs /auto_review on.
# "opt_out": automatic review is enabled until the user runs /auto_review off.
mode = "opt_in"
```

Choose the default according to the product's desired rollout policy.

For a conservative rollout, prefer:

```text
enabled = false
mode = "opt_in"
```

unless the existing requirements specify otherwise.

---

# 38. README Update

Update README configuration documentation to describe:

```text
[auto_review]
enabled
mode
```

and commands:

```text
/auto_review on
/auto_review on /ja
/auto_review on /en
/auto_review off
```

Document clearly that:

* `/auto_review on` immediately runs a review.
* Language arguments apply only to that review.
* Future automatic reviews use `default_language`.
* Preferences are stored per project/user.
* Manual `/review` remains available regardless of auto-review setting.

---

# 39. Logging

Add useful structured logs.

Example:

```csharp
_logger.LogInformation(
    "Auto review {EnabledState} for project {ProjectId}, user {UserId}",
    enabled,
    projectId,
    userId);
```

On automatic trigger:

```csharp
_logger.LogInformation(
    "Starting automatic review for project {ProjectId}, merge request {MergeRequestId}, author {UserId}",
    projectId,
    mergeRequestId,
    userId);
```

Do not log secrets or access tokens.

---

# 40. Do Not Use Logs as Preference Storage

SQLite is authoritative.

Preferences must survive application restart.

Do not reconstruct state from past commands or logs.

---

# 41. Cancellation

Propagate `CancellationToken` through:

```text
setting lookup
setting upsert
policy evaluation
command handling
opened-event handling
review invocation
```

Do not weaken the existing review cancellation semantics.

---

# 42. Tests

Add focused automated tests.

At minimum implement the following.

## Test 1 — Opt-In Default Is Off

Given:

```toml
enabled = true
mode = "opt_in"
```

and no DB row:

```text
IsEnabled = false
```

---

## Test 2 — Opt-Out Default Is On

Given:

```toml
enabled = true
mode = "opt_out"
```

and no DB row:

```text
IsEnabled = true
```

---

## Test 3 — Global Disabled Always Wins

Given:

```toml
enabled = false
```

and DB:

```text
Enabled = true
```

verify:

```text
effective auto review = false
```

---

## Test 4 — Explicit ON Overrides Opt-In Default

Store:

```text
Enabled = true
```

under opt-in.

Verify effective state is true.

---

## Test 5 — Explicit OFF Overrides Opt-Out Default

Store:

```text
Enabled = false
```

under opt-out.

Verify effective state is false.

---

## Test 6 — Preferences Are Project-Specific

Create:

```text
Project A / User 123 = ON
Project B / User 123 = OFF
```

Verify independent results.

---

## Test 7 — `/auto_review on`

Run:

```text
/auto_review on
```

Verify:

* DB setting becomes true.
* Existing review pipeline is invoked.
* Language equals `default_language`.

---

## Test 8 — `/auto_review on /ja`

Verify:

```text
DB setting = true
review invoked
language = ja
```

---

## Test 9 — `/auto_review on /en`

Verify:

```text
DB setting = true
review invoked
language = en
```

---

## Test 10 — `/auto_review off`

Verify:

```text
DB setting = false
review pipeline not invoked
```

---

## Test 11 — Manual `/review` Ignores Auto-Review OFF

Store:

```text
auto review = false
```

Run:

```text
/review
```

Verify the review still starts.

---

## Test 12 — Manual `/review /ja`

Verify existing behavior remains unchanged.

---

## Test 13 — Open Event with Enabled Preference

Create an opened PR/MR from a user whose effective auto-review setting is true.

Verify:

* Existing normal review pipeline starts.
* Language equals `default_language`.

---

## Test 14 — Open Event with Disabled Preference

Verify no review starts.

---

## Test 15 — Open Event Uses Author User ID

Create an event where webhook sender and PR/MR author differ.

Verify policy lookup uses the PR/MR author.

---

## Test 16 — GitHub and GitLab Use Same Policy

Provide equivalent normalized opened events from both providers.

Verify the same `IAutoReviewPolicy` behavior is used.

---

## Test 17 — Same Numeric User ID Across Providers Is Not Shared Globally

Ensure project/provider isolation prevents accidental preference sharing.

---

## Test 18 — Invalid Language Handling

Run:

```text
/auto_review on /invalid
```

Verify behavior matches existing `/review /invalid` validation semantics.

Do not silently persist ON and then fail review in an inconsistent state unless that ordering is explicitly intended.

Prefer validating command syntax/language before mutating persistent state.

---

# 43. Important Ordering for `/auto_review on /lang`

Validate the entire command before persisting the setting.

Preferred flow:

```text
Parse command
      |
      v
Validate on/off
      |
      v
Validate optional language
      |
      v
Check feature enabled
      |
      v
Persist ON
      |
      v
Start review
```

This prevents malformed input such as:

```text
/auto_review on /invalid
```

from unexpectedly changing user preference.

---

# 44. Review Failure Does Not Roll Back Preference

Once a valid:

```text
/auto_review on
```

command successfully persists the setting, a later review failure must not revert it.

These are separate operations:

```text
preference update
review execution
```

Example:

```text
AutoReview ON saved successfully
review model later fails
```

Expected:

```text
AutoReview remains ON
```

---

# 45. Open-Event Review Failure Does Not Change Preference

If an automatically triggered review fails, do not change:

```text
Enabled
```

in the user setting.

Review success/failure is unrelated to preference state.

---

# 46. Existing Review Telemetry

Automatically triggered reviews should flow through the same normal review execution path and therefore produce normal telemetry:

```text
ReviewExecution
ReviewTurn
RuleSearchExecution
ReviewRuleUsage
```

where applicable.

Do not create a parallel telemetry path.

If the system already records trigger source, it may distinguish manual/automatic review.

Do not add a new telemetry field solely for this feature unless genuinely required.

---

# 47. No User-Language Persistence

Do not add:

```text
preferred_language
review_language
default_language_override
```

to `auto_review_user_settings`.

The desired model is:

```text
SQLite:
    explicit enabled/disabled choice

config.toml:
    default review language

command:
    one-review language override
```

Keep these responsibilities separate.

---

# 48. Out of Scope

Do not implement:

* User-specific persistent language preferences
* Organization-wide user preferences
* Global cross-project user profiles
* Auto-review schedules
* Branch-specific auto-review policies
* File-path-specific policies
* Automatic reviewer assignment
* Per-user model selection
* Auto-review billing/quota logic
* New review pipeline
* New result-comment mechanism
* New statistics dashboard changes unrelated to this feature

Keep the implementation focused on automatic trigger preference and command integration.

---

# 49. Recommended Implementation Order

Use the following order unless the existing architecture strongly suggests a small adjustment.

```text
1. Inspect existing /review command parser
2. Inspect language parsing/validation
3. Inspect GitHub/GitLab opened-event normalization
4. Identify stable user ID in both providers
5. Inspect existing per-MR review concurrency controls
6. Add [auto_review] configuration options
7. Add configuration validation
8. Add auto_review_user_settings migration
9. Add repository model/API
10. Implement SQLite Get/Set
11. Add IAutoReviewPolicy
12. Extend command parser for /auto_review
13. Implement /auto_review on
14. Reuse existing language resolution
15. Invoke existing review pipeline from /auto_review on
16. Implement /auto_review off
17. Integrate policy into PR/MR opened events
18. Ensure opened event uses author User ID
19. Reuse persistent review-status-comment flow
20. Reuse existing per-MR review lock
21. Update config.template.toml
22. Update README
23. Add repository/policy tests
24. Add command tests
25. Add GitHub opened-event tests
26. Add GitLab opened-event tests
27. Run full existing test suite
28. Fix regressions without expanding scope
```

---

# 50. Acceptance Criteria

This change is complete when all of the following are true:

* `[auto_review]` configuration exists.
* Auto review has a global `enabled` switch.
* Auto review supports `opt_in`.
* Auto review supports `opt_out`.
* Explicit user preference is stored in SQLite.
* Preference key is `(ProjectId, UserId)`.
* Stable provider user IDs are used.
* GitHub and GitLab use the same policy layer.
* `/auto_review on` is supported.
* `/auto_review on /ja` is supported.
* `/auto_review on /en` is supported.
* `/auto_review off` is supported.
* `/auto_review on` immediately enters the existing review sequence.
* Language-less auto-review commands use `default_language`.
* Explicit command language overrides apply only to that review.
* Future opened-event automatic reviews use `default_language`.
* `/auto_review off` does not start a review.
* Manual `/review` works regardless of automatic-review preference.
* PR/MR opened events use the author's preference.
* Global disabled state prevents automatic reviews even when DB override is ON.
* Opt-in users without DB state default to OFF.
* Opt-out users without DB state default to ON.
* Explicit DB state overrides mode defaults.
* Preference survives application restart.
* Review failures do not modify preference.
* Automatic reviews reuse the normal review pipeline.
* Automatic reviews reuse the normal owned status-comment mechanism.
* Existing multi-project isolation remains intact.
* Automated tests cover configuration, policy, commands, providers, language, and project isolation.
* Existing tests pass.

---

# 51. Coding Guidelines

Follow existing:

* Command parsing conventions
* Language validation
* Project identity handling
* GitHub/GitLab provider abstraction
* SQLite conventions
* Migration conventions
* Repository patterns
* Options/configuration patterns
* Dependency injection
* Structured logging
* CancellationToken propagation
* Review concurrency controls
* Test infrastructure

Prefer one shared review execution path for:

```text
/review
/auto_review on
PR/MR opened automatic review
```

Do not duplicate review logic.

The core principle is:

> Auto review decides when the existing review pipeline starts; it does not create a new kind of review.

---

# 52. Final Deliverable

After implementation, provide a concise implementation report containing:

1. Files changed.
2. `config.toml` options added.
3. Final `auto_review_user_settings` schema.
4. Repository API added.
5. Auto-review policy logic.
6. Opt-in semantics.
7. Opt-out semantics.
8. How GitHub/GitLab user IDs are normalized.
9. Command grammar implemented.
10. How `/auto_review on` reuses normal review execution.
11. Language resolution behavior.
12. How PR/MR opened events invoke automatic review.
13. How manual `/review` remains independent.
14. How concurrency with manual review is handled.
15. How persistent review-status comments are reused.
16. README/config template changes.
17. Tests added or updated.
18. Any remaining risks or assumptions.

Do not expand the scope into unrelated review, statistics, or rule-learning changes.
