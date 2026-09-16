# AI Code Review System – Multi-Project Usage Statistics Roadmap

## 1. Objective

Extend the existing ASP.NET AI code review system with a usage statistics and monitoring subsystem that:

* Supports multiple projects cleanly.
* Tracks review usage, model usage, latency, and errors.
* Tracks the two-stage review pipeline separately.
* Tracks learned-rule lifecycle and effectiveness.
* Measures full-table vector search cost as the learned-rule dataset grows.
* Provides project-level and cross-project dashboards through ASP.NET web pages.
* Reuses the existing SQLite database.
* Keeps statistics collection isolated from the core review logic.

The initial goal is observability and operational insight rather than billing, distributed telemetry, or external analytics infrastructure.

---

# 2. Current System Assumptions

## Review Flow

A normal code review uses a regular model in two turns.

### Turn 1 — Detection

The model identifies review candidates and assigns one of:

* Critical
* Major
* Minor

### Turn 2 — Selection and Formatting

The second model call:

* Selects the findings worth reporting.
* Removes weak or redundant findings.
* Converts selected findings into the final review text.

Conceptually:

```text
Merge Request
    |
    v
Detection
    |
    | Critical / Major / Minor candidates
    v
Selection + Formatting
    |
    v
Final Review
```

---

## Learned Rule Flow

Project-specific rules are learned from merge events using a smaller model.

Existing rules are re-evaluated against merged changes.

Conceptually:

```text
Merge Event
    |
    v
Rule Extraction / Evaluation
    |
    +-- bad pattern still exists
    |       -> decrease confidence
    |
    +-- bad pattern disappeared
            -> increase confidence
```

Rules expire by default after approximately three months when they are both:

* Old enough.
* Below the configured minimum confidence score.

Expired rules are removed from the active `learned_rules` table.

---

## Rule Retrieval

Learned rules are currently retrieved by:

1. Loading the relevant learned-rule set.
2. Performing vector similarity comparison.
3. Selecting candidate rules.
4. Passing selected rules into the review process.

The current implementation performs vector search by scanning the stored rule set.

Because this may become a scalability bottleneck as projects and learned rules increase, rule-count and search-latency statistics must be collected before introducing a more complex vector-storage solution.

---

# 3. Multi-Project Design Principles

Multi-project support should be introduced at the storage and service layers before building the statistics dashboard.

Every project-specific entity must have an explicit project association.

The following data must never be implicitly global:

* Merge requests
* Review executions
* Learned rules
* Rule-learning history
* Rule-search executions
* Review-rule usage
* Project statistics

The system should allow both:

```text
Single Project View
```

and:

```text
All Projects View
```

without duplicating data or maintaining separate databases.

---

# 4. Phase 1 — Introduce the Project Entity

## Goal

Establish a stable project identity that can be referenced by all review, rule, and statistics records.

## Add a `projects` Table

Recommended minimum schema:

```text
projects
--------------------------------
id
external_project_id
name
display_name
repository_url
is_active
created_at
updated_at
```

`external_project_id` should represent the stable project identifier from the source system, such as GitLab or another SCM provider.

Do not rely on the project name as the primary identifier because repositories can be renamed.

---

## Add `project_id` to Existing Project-Specific Data

At minimum, associate the following with `project_id`:

```text
learned_rules
review executions
merge events
rule extraction operations
```

Any current uniqueness constraints must be reviewed.

For example, a rule identifier that was previously globally unique may need to become:

```text
UNIQUE(project_id, rule_id)
```

or use an internal globally unique database key while retaining project-local rule IDs.

---

## Repository Layer

Introduce explicit project scoping.

Avoid APIs such as:

```csharp
GetLearnedRulesAsync()
```

Prefer:

```csharp
GetLearnedRulesAsync(
    long projectId,
    CancellationToken cancellationToken);
```

Likewise:

```csharp
IncrementConfidenceByChunkIdAsync(
    long projectId,
    string ruleId,
    CancellationToken cancellationToken);
```

```csharp
DecrementConfidenceByChunkIdAsync(
    long projectId,
    string ruleId,
    CancellationToken cancellationToken);
```

The repository must make accidental cross-project queries difficult.

---

## Deliverables

* `projects` table.
* Migration for existing installations.
* Existing data assigned to a default project where necessary.
* Project-aware learned-rule repository.
* Project-aware merge handling.
* Project-aware review pipeline.

---

# 5. Phase 2 — Add Review Execution Statistics

## Goal

Record one top-level execution for every reviewed merge request.

Add:

```text
review_executions
--------------------------------
id
project_id
merge_request_id

started_at
completed_at
duration_ms

candidate_finding_count
selected_finding_count

critical_count
major_count
minor_count

status
error_type
```

Recommended relationship:

```text
projects
   |
   +-- review_executions
```

---

## Important Metrics

At this level, track:

* Number of reviews.
* Number of reviewed merge requests.
* Total review duration.
* Candidate finding count.
* Final selected finding count.
* Critical count.
* Major count.
* Minor count.
* Failed review count.

Derived metric:

```text
Selection Rate =
Selected Finding Count
/
Candidate Finding Count
```

This provides a useful signal for detecting excessive noise from the first review stage.

---

# 6. Phase 3 — Track the Two Review Turns Separately

## Goal

Measure the cost and behavior of Detection and Selection independently.

Add:

```text
review_turns
--------------------------------
id
review_execution_id

turn_type
model

input_tokens
output_tokens
duration_ms

finding_count
success
error_type

created_at
```

Supported values:

```text
Detection
Selection
```

Conceptually:

```text
ReviewExecution
    |
    +-- Detection
    |
    +-- Selection
```

---

## Statistics to Expose

Per project and globally:

```text
Detection
- Calls
- Average input tokens
- Average output tokens
- Average latency
- Average candidate findings

Selection
- Calls
- Average input tokens
- Average output tokens
- Average latency
- Average final findings
```

Also expose:

```text
Candidate -> Final Selection Rate
```

This should be one of the primary review-quality indicators.

---

# 7. Phase 4 — Add Learned Rule Lifecycle Statistics

## Goal

Preserve rule history even after active learned rules expire and are deleted.

Add:

```text
rule_learning_events
--------------------------------
id
project_id
rule_id

merge_request_id

event_type

confidence_before
confidence_after

created_at
```

Supported event types:

```text
Created
ConfidenceIncreased
ConfidenceDecreased
Expired
```

The existing `learned_rules` table remains the active working set.

`rule_learning_events` becomes the historical audit and statistics stream.

---

## Update the Existing Confidence Logic

The current confidence update flow should additionally emit a lifecycle event.

Conceptually:

```text
Load current confidence
        |
        v
Evaluate merged diff
        |
        +-- pattern still present
        |      |
        |      v
        |  confidence -
        |
        +-- pattern removed
               |
               v
           confidence +
        |
        v
Write RuleLearningEvent
```

---

## Rule Expiration

Keep expired rules out of the active vector-search table.

Before deletion, record:

```text
EventType = Expired
```

This preserves historical information without increasing active vector-search cost.

---

# 8. Phase 5 — Make Rule Retrieval Project-Aware

## Goal

Prevent rule leakage between projects and establish meaningful per-project search metrics.

Rule retrieval must only search rules belonging to the current project unless explicit cross-project sharing is added in the future.

Current flow:

```text
All learned rules
    |
    v
Vector search
```

Target flow:

```text
Project
    |
    v
Project learned rules
    |
    v
Vector similarity
    |
    v
Candidate rules
    |
    v
Rules supplied to review
```

The SQL filtering step should happen before vector comparison:

```sql
SELECT ...
FROM learned_rules
WHERE project_id = @projectId;
```

This is important for both correctness and performance.

---

# 9. Phase 6 — Add Rule Search Performance Statistics

## Goal

Measure the cost of the current full-scan vector search before attempting optimization.

Add:

```text
rule_search_executions
--------------------------------
id
review_execution_id
project_id

total_rule_count
candidate_rule_count
selected_rule_count

embedding_duration_ms
search_duration_ms

created_at
```

Definitions:

```text
total_rule_count
    Number of active project rules scanned.

candidate_rule_count
    Number of rules surviving similarity filtering.

selected_rule_count
    Number of rules actually supplied to the review prompt.
```

---

## Key Metric

Track:

```text
Active Rule Count
vs.
Vector Search Duration
```

per project.

This will provide evidence for deciding whether SQLite full-table vector search remains sufficient or whether a dedicated index/vector solution is justified later.

Do not replace the existing implementation solely because the number of projects increases.

Optimize only when actual measurements show a meaningful bottleneck.

---

# 10. Phase 7 — Track Rule Usage During Reviews

## Goal

Determine whether learned rules actually improve review output.

Add:

```text
review_rule_usage
--------------------------------
id
review_execution_id
project_id
rule_id

similarity_score

used_in_prompt
produced_candidate
produced_final_finding

created_at
```

This creates the following observable pipeline:

```text
Learned Rule
     |
     v
Matched by Vector Search
     |
     v
Included in Prompt
     |
     v
Detection Candidate
     |
     v
Selected Final Finding
```

---

## Derived Rule Metrics

### Candidate Hit Rate

```text
Produced Candidate
/
Used in Prompt
```

### Final Hit Rate

```text
Produced Final Finding
/
Used in Prompt
```

These metrics should be calculated per:

* Rule.
* Project.
* Time period.

This allows the system to identify rules that are frequently retrieved but rarely produce useful final findings.

---

# 11. Phase 8 — Introduce a Statistics Service Layer

## Goal

Keep statistics SQL and aggregation logic out of controllers and review services.

Introduce:

```csharp
IUsageRecorder
```

for write operations and:

```csharp
IStatisticsService
```

for reporting.

Example:

```csharp
public interface IStatisticsService
{
    Task<OverviewStatistics> GetOverviewAsync(
        long? projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<ReviewStatistics> GetReviewStatisticsAsync(
        long? projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<RuleStatistics> GetRuleStatisticsAsync(
        long? projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
```

A null `projectId` can represent the all-project aggregate view.

---

## Architecture

```text
Review Engine
     |
     v
UsageRecorder
     |
     v
SQLite


ASP.NET Dashboard
     |
     v
StatisticsService
     |
     v
SQLite
```

The dashboard must never contain raw SQL.

---

# 12. Phase 9 — Add the ASP.NET Statistics Dashboard

## Goal

Provide a lightweight web UI for system usage and behavior.

Razor Pages or MVC is sufficient initially.

A separate SPA is not required.

Recommended routes:

```text
/statistics
/statistics/reviews
/statistics/rules
/statistics/projects
```

---

# 13. Phase 10 — Add Global and Project Filters

## Goal

Make multi-project usage understandable without creating separate dashboards for every repository.

Add a shared filter:

```text
Project:
[ All Projects v ]

Period:
[ Last 7 Days v ]
```

Possible project values:

```text
All Projects
Project A
Project B
Project C
```

Every dashboard query should respect the selected project.

---

# 14. Dashboard — Overview

Route:

```text
/statistics
```

Show:

```text
Projects                  12
Reviews                  842
Merge Requests           731

Input Tokens            28.4M
Output Tokens            3.1M

Average Review          12.4 sec
Error Rate                0.8%

Candidates               3,421
Final Findings           1,287
Selection Rate            37.6%
```

Charts:

```text
Reviews over time
Tokens over time
Average review latency
Findings by severity
```

---

# 15. Dashboard — Reviews

Route:

```text
/statistics/reviews
```

Focus on the two-stage review pipeline.

Show:

```text
Detection

Calls
Average input tokens
Average output tokens
Average duration
Average candidates
```

and:

```text
Selection

Calls
Average input tokens
Average output tokens
Average duration
Average selected findings
```

Also show:

```text
Candidate -> Final Selection Rate
```

and severity distribution:

```text
Critical
Major
Minor
```

---

# 16. Dashboard — Learned Rules

Route:

```text
/statistics/rules
```

Show:

```text
Active Learned Rules
Rules Created
Rules Expired

Confidence Increases
Confidence Decreases

Average Rules Scanned
Average Search Duration
Average Candidate Rules
Average Prompt Rules
```

Also provide a rule table:

```text
Rule
Project
Confidence
Prompt Uses
Candidates
Final Findings
Candidate Hit Rate
Final Hit Rate
```

This page should become the primary observability surface for the learned-rule subsystem.

---

# 17. Dashboard — Projects

Route:

```text
/statistics/projects
```

Provide a cross-project comparison.

Example:

```text
Project       Reviews   Tokens   Avg Time   Active Rules
--------------------------------------------------------
Project A       210      8.2M      11.2s        184
Project B       183      7.1M      15.8s        421
Project C        89      2.4M       8.7s         52
```

Additional useful columns:

```text
Selection Rate
Final Findings / Review
Vector Search Duration
Error Rate
```

This view is especially useful for detecting a single repository whose rule set or review workload behaves abnormally compared with the others.

---

# 18. Phase 11 — Add Supporting SQLite Indexes

## Goal

Prevent statistics queries from degrading normal review performance as data accumulates.

At minimum, evaluate indexes equivalent to:

```text
review_executions(project_id, started_at)

review_turns(review_execution_id)

rule_learning_events(project_id, created_at)

rule_search_executions(project_id, created_at)

review_rule_usage(project_id, rule_id)

learned_rules(project_id)
```

Additional indexes should be added based on measured query plans, not preemptively.

---

# 19. Phase 12 — Add Statistics Retention Policy

## Goal

Avoid uncontrolled growth of high-volume execution telemetry.

Different data classes should have different retention behavior.

Recommended approach:

```text
learned_rules
    Active operational data.

rule_learning_events
    Long-lived historical data.

review_executions
    Long-lived aggregated execution history.

review_turns
    Medium/long-term usage history.

review_rule_usage
    Potentially high volume.

rule_search_executions
    Potentially high volume.
```

Initially, retain everything.

Add retention only after actual storage growth is measured.

If necessary, older detailed telemetry can later be aggregated into daily statistics.

---

# 20. Phase 13 — Optional Daily Aggregation

Do not implement this in the first version unless dashboard queries become slow.

Possible future table:

```text
daily_project_statistics
--------------------------------
date
project_id

review_count

detection_input_tokens
detection_output_tokens

selection_input_tokens
selection_output_tokens

candidate_count
selected_count

critical_count
major_count
minor_count

average_review_duration_ms

rule_search_count
average_rule_search_duration_ms
```

This can reduce dashboard aggregation cost while retaining raw data for a configurable period.

---

# 21. Phase 14 — Future Review Quality Feedback

This phase should remain optional until the SCM integration can reliably determine user reactions to review findings.

Potential signals:

```text
AI finding accepted
AI finding rejected
AI suggestion applied
AI comment resolved
```

This could eventually extend:

```text
review_rule_usage
```

with outcome information and enable stronger rule-quality metrics.

For example:

```text
Rule Retrieved
    |
    v
Candidate Produced
    |
    v
Final Finding
    |
    v
Accepted / Rejected
```

This should not block the initial statistics implementation.

---

# 22. Recommended Implementation Order

Implement in this order:

```text
Phase 1
Project entity and project scoping

        ↓

Phase 2
ReviewExecution

        ↓

Phase 3
ReviewTurn

        ↓

Phase 4
RuleLearningEvent

        ↓

Phase 5
Project-scoped learned-rule vector search

        ↓

Phase 6
RuleSearchExecution

        ↓

Phase 7
ReviewRuleUsage

        ↓

Phase 8
StatisticsService / UsageRecorder

        ↓

Phase 9
ASP.NET statistics pages

        ↓

Phase 10
Global / per-project filtering

        ↓

Phase 11
SQLite indexing

        ↓

Phase 12+
Retention and optional aggregation
```

---

# 23. Recommended Initial Scope

The first production-ready version should stop after Phase 11.

It should provide:

* Multi-project isolation.
* Global and project-specific statistics.
* Review count.
* Model/token usage.
* Detection vs Selection statistics.
* Severity statistics.
* Candidate-to-final selection rate.
* Learned-rule lifecycle history.
* Active learned-rule count.
* Vector-search latency.
* Number of scanned rules.
* Rule prompt usage.
* Rule candidate hit rate.
* Rule final hit rate.
* ASP.NET dashboard.
* SQLite indexes for the main reporting queries.

Do not initially add:

* External telemetry systems.
* Dedicated time-series databases.
* Dedicated vector databases.
* Distributed tracing infrastructure.
* Complex data warehouses.
* Automatic model-quality scoring.
* Cross-project learned-rule sharing.

The existing ASP.NET + SQLite architecture should remain sufficient until actual measurements indicate otherwise.

---

# 24. Target Architecture

```text
                         +----------------------+
                         |       Projects       |
                         +----------+-----------+
                                    |
                    +---------------+---------------+
                    |                               |
                    v                               v
           Review Pipeline                  Learned Rule Pipeline
                    |                               |
                    v                               v
           ReviewExecution                   learned_rules
             /          \                         |
            v            v                        v
       Detection      Selection            RuleLearningEvent
            |
            v
      Rule Search
            |
            +------------------+
            |                  |
            v                  v
   RuleSearchExecution   ReviewRuleUsage


                    SQLite
                       |
                       v
               StatisticsService
                       |
                       v
                ASP.NET Dashboard
                       |
          +------------+-------------+
          |            |             |
          v            v             v
       Overview      Reviews        Rules
          |
          v
       Projects
```

The key architectural rule is:

**Every operational and statistical record must be explicitly associated with a project whenever the underlying operation is project-specific.**

This keeps multi-project support predictable, prevents learned-rule leakage between repositories, and allows the same telemetry to serve both per-project debugging and system-wide monitoring.
