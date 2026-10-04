# Task: Add Persistent Per-Repository, Per-User Review Language Preferences

## Objective

Add persistent review-language preferences keyed by:

```text
Repository ID + User ID
```

The application must support two ways to explicitly update the user's preferred review language:

```text
/review_lang en
/review_lang ja
```

and:

```text
/review /en
/review /ja
```

When a supported language is explicitly specified using either form, save that language preference for the current repository and user.

When the user runs:

```text
/review
```

with no explicit language, do not modify the database.

Instead, resolve the language from:

```text
1. The user's stored language preference for this repository
2. The existing application default language
```

---

## Required Behavior

### 1. Dedicated language-setting command

Support commands such as:

```text
/review_lang en
/review_lang ja
```

When the language is supported:

```text
(repositoryId, userId) -> language
```

must be persisted.

Examples:

```text
/review_lang en
```

stores:

```text
(repositoryId, userId) -> "en"
```

and:

```text
/review_lang ja
```

stores:

```text
(repositoryId, userId) -> "ja"
```

This command should only update the user's review-language preference.

It must not trigger a review.

---

## 2. Explicit language on `/review`

The existing review command may also explicitly specify a language:

```text
/review /en
/review /ja
```

When a supported language is explicitly specified:

1. Persist the language preference for the current repository and user.
2. Run the requested review using that language.

For example:

```text
/review /ja
```

must behave conceptually as:

```text
save:
(repositoryId, userId) -> "ja"

then:
run review in "ja"
```

Similarly:

```text
/review /en
```

must store `"en"` and run the review in English.

---

## 3. `/review` without an explicit language

When the command is:

```text
/review
```

do not update the database.

Instead, determine the effective review language using:

```text
1. Stored user/repository language preference
2. Existing application default language
```

For example, if the database contains:

```text
(repositoryId, userId) -> "ja"
```

then:

```text
/review
```

must use `"ja"`.

The stored value must remain unchanged.

If no language preference exists for the user/repository pair, `/review` must use the application's existing default-language behavior.

Do not create a database record merely because `/review` falls back to the default language.

---

## Language Validation

The singleton `Settings` object is the source of truth for supported languages.

Use the existing method:

```csharp
public bool HasTemplate(string lang)
{
    return review1Templates_.ContainsKey(lang)
        && review2Templates_.ContainsKey(lang)
        && learnedTemplates_.ContainsKey(lang);
}
```

Validate explicit language values using:

```csharp
Context.Instance.Settings.HasTemplate(lang)
```

Do not introduce a duplicated hard-coded list such as:

```csharp
new[] { "en", "ja" }
```

`en` and `ja` are examples only.

The configured templates determine which languages are supported.

---

## Invalid Language Handling

Unsupported languages must never be persisted.

For example:

```text
/review_lang xx
/review /xx
```

must not create or overwrite a stored preference with `"xx"`.

Preserve the project's existing command/error handling conventions where possible.

If there is already a parser error or invalid-command response mechanism, reuse it.

Do not silently store an unsupported value.

---

## Preference Key

The logical storage key must be:

```text
(repository ID, user ID)
```

Preferences must be isolated across both users and repositories.

Example:

```text
Repository A + User 1 -> ja
Repository A + User 2 -> en
Repository B + User 1 -> en
```

Do not key preferences by:

```text
username
repository name
MR/PR number
branch name
comment ID
```

Use stable provider IDs.

---

## Storage

First inspect the repository for existing database tables, repositories, services, or persistence abstractions used for user/repository settings.

Reuse the existing persistence infrastructure.

Do not introduce a separate database technology or persistence framework solely for this feature.

Prefer a small abstraction such as:

```csharp
public interface IReviewLanguagePreferenceStore
{
    string? Get(long repositoryId, long userId);

    void Set(long repositoryId, long userId, string language);
}
```

or the asynchronous equivalent if the current persistence layer is asynchronous:

```csharp
public interface IReviewLanguagePreferenceStore
{
    Task<string?> GetAsync(long repositoryId, long userId);

    Task SetAsync(long repositoryId, long userId, string language);
}
```

Adapt identifier types and naming to match the existing codebase.

Do not put raw database access directly into `WebhookController`.

---

## Command Model

Inspect:

```text
ReviewCommand
ReviewCommandParser
ReviewCommandType
```

Add support for a dedicated command type for:

```text
/review_lang <language>
```

For example:

```csharp
ReviewCommandType.ReviewLanguage
```

or another name consistent with the existing enum naming.

The parsed command should retain the language value.

Conceptually:

```text
/review_lang ja

Type     = ReviewLanguage
Language = "ja"
```

For:

```text
/review /ja
```

the command should remain a review command:

```text
Type     = Review
Language = "ja"
```

For:

```text
/review
```

the parser should represent the absence of an explicit language.

Conceptually:

```text
Type     = Review
Language = null
```

Do not replace an omitted language with the application default during parsing.

The caller must be able to distinguish:

```text
/review /en
```

from:

```text
/review
```

because only the first command modifies persistent language state.

---

## Effective Language Resolution

Centralize language resolution where practical.

The effective language for a review should follow:

```text
if command has an explicit supported language:
    persist that language
    use that language

else if a stored supported preference exists:
    use the stored preference

else:
    use the existing application default language
```

Conceptually:

```csharp
string ResolveReviewLanguage(
    long repositoryId,
    long userId,
    string? explicitLanguage)
{
    if (explicitLanguage != null)
    {
        // Validation should already have happened.
        return explicitLanguage;
    }

    string? stored = preferenceStore.Get(repositoryId, userId);

    if (stored != null &&
        Context.Instance.Settings.HasTemplate(stored))
    {
        return stored;
    }

    return ExistingDefaultLanguageResolution();
}
```

Do not persist the fallback/default value.

---

## `/review_lang` Processing

The dedicated language command should follow this flow:

```text
Parse command
   |
   v
Extract repository ID + user ID
   |
   v
Validate language using Settings.HasTemplate()
   |
   +-- invalid --> use existing error handling
   |
   v
Persist:
(repository ID, user ID) -> language
   |
   v
Return success
```

It must not queue a review task.

If the existing system posts acknowledgement comments for similar configuration commands, follow the same convention.

Avoid introducing a new response style unless necessary.

---

## `/review /<language>` Processing

Explicit review-language commands should follow:

```text
Parse:
/review /ja
   |
   v
Extract repository ID + user ID
   |
   v
Validate "ja"
   |
   v
Persist:
(repository ID, user ID) -> "ja"
   |
   v
Queue review using "ja"
```

The persistence should happen before the review task consumes the effective language.

---

## `/review` Processing

Plain review commands should follow:

```text
Parse:
/review
   |
   v
Extract repository ID + user ID
   |
   v
Read stored preference
   |
   +-- valid preference exists
   |       |
   |       v
   |     use it
   |
   +-- no valid preference
           |
           v
       use existing default
   |
   v
Queue review
```

There must be no database write in this path solely for language selection.

---

## GitLab and GitHub

Implement equivalent semantics for both GitLab Merge Requests and GitHub Pull Requests.

Provider-specific code may be responsible for extracting:

```text
repository ID
user ID
```

but language preference behavior must be the same.

The same logical command must produce the same behavior on both providers.

For example:

```text
/review_lang ja
```

must update the preference on both GitLab and GitHub.

Similarly:

```text
/review /ja
```

must both save `"ja"` and run the review using `"ja"`.

And:

```text
/review
```

must only read the stored preference and never overwrite it.

---

## Keep Controller Logic Small

`WebhookController` should remain responsible primarily for:

```text
webhook validation
event parsing
command dispatch
task scheduling
```

Do not add significant persistence or language-resolution business logic directly to the controller.

Prefer dedicated services/tasks such as:

```text
ReviewLanguagePreferenceService
ReviewLanguageCommandTask
ReviewLanguageResolver
```

only where they fit the existing architecture.

Avoid unnecessary abstractions if an existing service can naturally own the behavior.

---

## Concurrency

Webhook requests may execute concurrently.

Language preference reads and writes must therefore follow the concurrency guarantees of the existing database/persistence layer.

Do not rely on the controller's unrelated locks to protect language-preference state.

If an upsert is required, implement it atomically using the persistence mechanism already used by the project.

There must be at most one logical preference record for each:

```text
(repositoryId, userId)
```

pair.

---

## Database Semantics

Prefer an upsert-style model.

Conceptually:

```text
(repositoryId, userId) is unique
```

Updating the language should replace the existing preference for that pair.

For example:

```text
current:
(repo1, user1) -> ja
```

then:

```text
/review_lang en
```

should result in:

```text
(repo1, user1) -> en
```

not two separate records.

Use an appropriate unique constraint if this fits the existing database architecture.

---

## Stale Stored Languages

A stored preference may become invalid if the corresponding templates are removed from the configuration.

Therefore, when reading a stored preference, validate it using:

```csharp
Context.Instance.Settings.HasTemplate(storedLanguage)
```

If it is no longer supported:

```text
do not use it
```

and fall back to the application's default-language behavior.

Do not automatically overwrite or delete the stored value unless the existing settings architecture explicitly follows that pattern.

---

## Tests

Add tests for at least the following scenarios.

### Parser

```text
/review_lang en
    -> ReviewLanguage command, language = en

/review_lang ja
    -> ReviewLanguage command, language = ja

/review /en
    -> Review command, explicit language = en

/review /ja
    -> Review command, explicit language = ja

/review
    -> Review command, no explicit language
```

---

### Dedicated preference command

```text
/review_lang ja
    -> stores ja

/review_lang en
    -> stores en

/review_lang unsupported-language
    -> does not store anything
```

---

### Explicit review language

```text
/review /ja
    -> stores ja
    -> review uses ja

/review /en
    -> stores en
    -> review uses en
```

---

### Plain review

Given:

```text
stored preference = ja
```

then:

```text
/review
```

must:

```text
use ja
not write to the database
```

Given no stored preference:

```text
/review
```

must:

```text
use existing application default language
not create a preference
```

---

### Existing preference must not be overwritten

Given:

```text
stored preference = ja
```

then:

```text
/review
```

must leave it as:

```text
ja
```

Likewise, application-default resolution must never overwrite it.

---

### Preference isolation

Verify:

```text
same repository + different users
    -> independent preferences
```

and:

```text
same user + different repositories
    -> independent preferences
```

---

### Update behavior

Given:

```text
(repo1, user1) -> ja
```

then:

```text
/review_lang en
```

must update the same logical record to:

```text
(repo1, user1) -> en
```

---

### Stale preference

Given:

```text
stored preference = xx
Settings.HasTemplate("xx") == false
```

then:

```text
/review
```

must use the existing default language.

---

## Backward Compatibility

Existing:

```text
/review
```

behavior must continue to work for users who have no stored preference.

The only difference is that a stored per-user/per-repository preference now takes precedence over the application default.

Do not change unrelated review behavior.

Do not make broad refactors.

Do not change webhook routing or background queue architecture unless required to propagate the resolved language.

---

## Recommended Responsibility Split

Prefer a structure conceptually similar to:

```text
ReviewCommandParser
    |
    v
ReviewCommand
    |
    +-- ReviewLanguage
    |       |
    |       v
    |   Preference Store
    |
    +-- Review
            |
            v
    ReviewLanguageResolver
            |
            +-- explicit language
            +-- stored preference
            +-- application default
            |
            v
        Review Task
```

The important separation is:

```text
parsing
persistence
language resolution
review execution
```

Do not conflate them unless the existing architecture already combines those responsibilities.

---

## Definition of Done

The task is complete when all of the following are true:

1. `/review_lang <lang>` updates the user's language preference.
2. `/review_lang <lang>` does not trigger a review.
3. `/review /<lang>` updates the user's language preference.
4. `/review /<lang>` runs the review using the specified language.
5. `/review` never modifies the language preference.
6. `/review` uses the stored preference when one exists.
7. `/review` uses the existing default language when no stored preference exists.
8. Preferences are keyed by repository ID and user ID.
9. Supported languages are validated through `Settings.HasTemplate`.
10. Unsupported languages are never persisted.
11. Stored stale/unsupported languages are not used.
12. GitLab and GitHub implement equivalent semantics.
13. Preference updates are concurrency-safe.
14. Existing review behavior remains backward-compatible.
15. Automated tests cover parser behavior, persistence, fallback behavior, isolation, update behavior, and both Git providers.
16. All existing and new tests pass.

Before implementing, inspect the existing command parser, `ReviewCommand`, review task flow, GitLab/GitHub payload models, database models, repositories/services, migrations, and existing default-language resolution logic.

Reuse the current architecture wherever possible and keep the implementation focused on this feature.