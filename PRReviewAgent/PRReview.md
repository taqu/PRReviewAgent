# Refactoring Plan: Simplify GitLab Review Pipeline Using AST Context

## Objective

Refactor the GitLab review pipeline to eliminate unnecessary LLM preprocessing steps.

The new pipeline should rely on deterministic path analysis and AST extraction, using the LLM only for the final code review.

---

# Target Pipeline

```
GitLab Merge Request
        Да
        Бе
Fetch Changed Files
        Да
        Бе
Resolve Related Files
        Да
        Бе
AstContextExtractor.Run(...)
        Да
        Бе
Build File Groups
        Да
        Бе
Prompt Builder
        Да
        Бе
LLM Review
        Да
        Бе
Merge Reviews
        Да
        Бе
Post GitLab Comment
```

---

# 1. Resolve Related Files

Implement a component that resolves the counterpart source/header file for each changed file.

Expected mappings include:

```
foo.cpp  <->  foo.h
foo.cpp  <->  foo.hpp
foo.c    <->  foo.h
foo.cc   <->  foo.hpp
```

Directory mappings should also be supported.

Examples:

```
include/
inc/
headers/

Бл

src/
source/
```

## Resolution order

1. Search among changed files in the current Merge Request.
2. If not found, generate candidate paths.
3. Query the GitLab repository for existing candidates.
4. If no counterpart exists, continue without one.

This component should be deterministic and must not use an LLM.

---

# 2. AST Extraction

For every review target, invoke:

```csharp
(string json, string diff) =
    AstContextExtractor.Run(
        filePath,
        fileContent,
        diffPath,
        diffContent,
        relatedPath,
        relatedContent);
```

This function already produces:

* Semantic JSON
* Expanded Diff

No additional semantic analysis should be performed.

The existing ContextCollector should be removed.

---

# 3. Build Review Groups

Remove the AI Planner.

Instead, construct review groups deterministically.

Typical examples:

```
Renderer.cpp
Renderer.h
```

Бл

```
Renderer
```

or

```
Texture.cpp
Texture.h
Texture.inl
```

Бл

```
Texture
```

Each group represents one logical review topic.

No LLM should be involved.

---

# 4. Prompt Builder

Generate one review prompt for each FileGroup.

Each file should contain:

````
## <filename>

Semantic Context

```json
...
````

Expanded Diff

```cpp
...
```

```

Do not include the original unified diff.

The expanded diff is the only source code that should be provided.

---

# 5. Execute Review

Invoke the review model once for each FileGroup.

Each prompt should review only the files contained in that group.

The review instructions remain unchanged.

---

# 6. Merge Results

Concatenate all generated reviews into a single Markdown document.

Example:

```

## Renderer

...

---

## Texture

...

---

## MemoryManager

...

```

Post the combined review as a GitLab comment.

---

# Implementation Goals

- Remove the Summarizer stage.
- Remove the Planner stage.
- Remove ContextCollector.
- Keep PromptBuilder.
- Keep Executor.
- Keep GitLab comment posting.

The only LLM invocation in the entire pipeline should be the final code review.

---

# Expected Benefits

- Lower latency
- Lower token usage
- Deterministic grouping
- Simpler architecture
- Better reproducibility
- Easier maintenance
- Stable review quality independent of preprocessing LLM output

The AST extractor becomes the single source of semantic context, while the LLM focuses exclusively on reviewing the code.
```
