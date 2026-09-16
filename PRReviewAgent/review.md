# Phase 9 Implementation Instructions — ASP.NET Statistics Dashboard

## Objective

Implement Phase 9 of the usage statistics roadmap by adding an ASP.NET web dashboard for the telemetry and aggregate statistics exposed by Phase 8.

Phase 8 already provides a dedicated `IStatisticsService` and reporting DTOs.

Phase 9 must build a lightweight, maintainable web UI over that service without duplicating SQL or aggregation logic in controllers, Razor Pages, or views.

The main outcome must be:

> Operators can inspect review usage, model/token usage, review quality signals, learned-rule behavior, rule-search performance, and project-level comparisons from ASP.NET web pages.

Do not redesign the statistics service.

Do not add new telemetry collection in this phase.

---

# 1. Prerequisites

Phases 1 through 8 are assumed to be complete.

The application should already have:

* `projects`
* `learned_rules`
* `review_executions`
* `review_turns`
* `rule_learning_events`
* `rule_search_executions`
* `review_rule_usage`
* `IStatisticsService`
* Statistics DTOs
* Project-aware statistics queries
* Time-range-aware statistics queries
* Overview aggregation
* Review aggregation
* Rule aggregation
* Project comparison aggregation
* Trend/time-series aggregation

The dashboard must consume these existing APIs.

---

# 2. Scope

Implement only the following:

1. Add ASP.NET statistics routes/pages.
2. Add a shared statistics layout/navigation entry if appropriate.
3. Add an overview page.
4. Add a review statistics page.
5. Add a learned-rule statistics page.
6. Add a project comparison page.
7. Add summary metric cards.
8. Add charts for key time-series and category metrics.
9. Add tables for detailed statistics.
10. Add empty/error states.
11. Add responsive styling consistent with the existing application.
12. Add automated tests for page routing and service integration.
13. Keep all aggregation logic inside `IStatisticsService`.

Phase 10 will add richer global project/date filters if they are not already naturally required by the current page implementation.

---

# 3. Technology Choice

Use the existing ASP.NET UI style already used by the project.

Prefer:

* Razor Pages if the application already uses Razor Pages.
* MVC views if the application already uses MVC.

Do not introduce a separate SPA framework solely for statistics.

Do not add React, Vue, Angular, or another frontend application unless the existing project already uses it.

Keep the dashboard part of the current ASP.NET application.

---

# 4. Recommended Routes

Add the following routes or their equivalent using existing routing conventions:

```text
/statistics
/statistics/reviews
/statistics/rules
/statistics/projects
```

Suggested meanings:

```text
/statistics
    Overall system summary

/statistics/reviews
    Detection / Selection review pipeline statistics

/statistics/rules
    Learned-rule lifecycle, search, and effectiveness statistics

/statistics/projects
    Multi-project comparison
```

Use route naming conventions already established in the application.

---

# 5. Shared Dashboard Navigation

Provide a simple statistics navigation element.

Recommended tabs or links:

```text
Overview
Reviews
Rules
Projects
```

The active page should be visually clear.

Do not build a separate navigation framework if the application already has one.

Integrate with the existing layout where appropriate.

---

# 6. No SQL in the Web Layer

Controllers, Razor PageModels, and views must not query telemetry tables directly.

Incorrect:

```csharp
SELECT ...
FROM review_executions
```

inside a controller or page model.

Required:

```csharp
var statistics =
    await _statisticsService.GetOverviewAsync(
        query,
        cancellationToken);
```

All metric definitions must remain owned by Phase 8.

---

# 7. Page Models / View Models

The web layer may introduce display-oriented view models, but they should wrap Phase 8 DTOs rather than reproduce business calculations.

Good:

```csharp
public sealed class StatisticsOverviewViewModel
{
    public OverviewStatistics Statistics { get; init; }

    public IReadOnlyList<ReviewTrendPoint> ReviewTrend { get; init; }

    public IReadOnlyList<TokenTrendPoint> TokenTrend { get; init; }
}
```

Avoid:

```csharp
public double CalculateSelectionRate()
```

if that rate already comes from `IStatisticsService`.

---

# 8. Default Reporting Range

The dashboard needs a deterministic initial time range.

Use a simple default such as:

```text
Last 30 days
```

unless the current application already has a standard reporting period.

Do not hardcode local-time assumptions inconsistently.

The PageModel/controller should construct a `StatisticsQuery` and pass it to the service.

Phase 10 may expand the filter experience.

---

# 9. Overview Page

Route:

```text
/statistics
```

This should be the primary entry point.

Display top-level cards for:

```text
Projects
Reviews
Merge Requests
Input Tokens
Output Tokens
Average Review Duration
Error Rate
Candidates
Final Findings
Selection Rate
```

Do not overload the first page with every available metric.

Prioritize system health and usage.

---

# 10. Overview Metric Cards

A recommended first row:

```text
Reviews
Input Tokens
Output Tokens
Avg Review Time
```

A second row may contain:

```text
Candidate Findings
Final Findings
Selection Rate
Error Rate
```

If project count is important, place it near the page heading or first row.

Use the existing design system or CSS conventions.

---

# 11. Numeric Formatting

The view layer may format raw DTO values for readability.

Examples:

```text
28400000 -> 28.4M
12400 ms -> 12.4 s
0.376 -> 37.6%
```

Do not change the underlying numeric values.

Formatting belongs in the web layer.

Reuse existing formatting helpers if available.

---

# 12. Duration Formatting

Use clear human-readable formatting.

Examples:

```text
840 ms
1.8 s
12.4 s
```

Avoid unnecessary precision.

The raw service value remains milliseconds.

---

# 13. Percentage Formatting

Display rates such as:

```text
Selection Rate
Error Rate
Candidate Hit Rate
Final Hit Rate
```

as percentages.

Example:

```text
0.376
```

becomes:

```text
37.6%
```

If the service returns `NULL`, display something like:

```text
—
```

or:

```text
N/A
```

Do not display `0%` for an undefined denominator.

---

# 14. Overview Charts

Add a small number of useful charts.

Recommended:

```text
Reviews over time
Token usage over time
```

Optionally:

```text
Findings over time
```

Do not turn the overview page into a dense analytics console.

---

# 15. Chart Library

If the application already has a chart library, reuse it.

Otherwise, a lightweight library such as Chart.js is acceptable.

Do not introduce a heavyweight visualization framework.

Keep chart setup simple and local to the statistics pages.

---

# 16. Chart Data Flow

Pass chart data from the PageModel/controller into the view.

Avoid making the browser reconstruct business statistics.

Good:

```text
ReviewTrendPoint[]
TokenTrendPoint[]
```

Avoid requiring the browser to fetch raw review telemetry and aggregate it.

---

# 17. Reviews Page

Route:

```text
/statistics/reviews
```

This page should focus on the two-turn review pipeline.

Clearly separate:

```text
Detection
Selection
```

The page should make it easy to compare them.

---

# 18. Detection Section

Display:

```text
Calls
Successful Calls
Failed Calls
Input Tokens
Output Tokens
Average Duration
Average Candidate Findings
```

Use the Phase 8 `ReviewTurnStatistics`.

---

# 19. Selection Section

Display:

```text
Calls
Successful Calls
Failed Calls
Input Tokens
Output Tokens
Average Duration
Average Selected Findings
```

Use the same visual structure as Detection so comparison is easy.

---

# 20. Candidate-to-Final Funnel

Display:

```text
Candidate Findings
Final Findings
Selection Rate
```

This may be represented as:

* Metric cards
* A simple funnel-like visual
* A compact horizontal comparison

Do not introduce a complex chart solely for decoration.

---

# 21. Severity Distribution

Display final finding totals by severity:

```text
Critical
Major
Minor
```

A bar or doughnut chart is acceptable.

The data must come from Phase 8 statistics.

Do not recalculate from raw findings in the browser.

---

# 22. Model Usage Section

If Phase 8 exposes model usage statistics, add a table such as:

```text
Model
Calls
Input Tokens
Output Tokens
Avg Duration
Failures
```

This is useful even if both turns currently use the same regular model.

The system should remain ready for future model configuration changes.

---

# 23. Rules Page

Route:

```text
/statistics/rules
```

This page should focus on three distinct areas:

1. Current learned-rule state
2. Rule lifecycle activity
3. Rule search/effectiveness

Do not mix these concepts without labels.

---

# 24. Rule Summary Cards

Recommended cards:

```text
Active Rules
Rules Created
Rules Expired
Confidence Increases
Confidence Decreases
```

Remember:

```text
Active Rules
```

is current-state data.

The event counts are time-range data.

If the page displays both, label them clearly.

---

# 25. Rule Search Performance Section

Display:

```text
Average Rules Scanned
Average Candidate Rules
Average Selected Rules
Average Search Duration
Average Embedding Duration
```

If embedding duration is unavailable/null, display:

```text
—
```

Do not imply zero work.

---

# 26. Rule Search Funnel

A compact representation may show:

```text
Rules Scanned
    ↓
Candidates
    ↓
Selected for Prompt
```

Use average counts for the requested period if that is what the Phase 8 DTO exposes.

Clearly label them as averages.

---

# 27. Rule Usage Table

Display a rule-level table using Phase 8 rule usage statistics.

Recommended columns:

```text
Project
Rule
Candidate Matches
Prompt Uses
Produced Candidate
Produced Final
Candidate Hit Rate
Final Hit Rate
```

For a single-project context, the Project column may be omitted.

For all-project context, project identity must remain visible.

---

# 28. Expired Rule Display

Historical rule usage may exist for rules no longer present in `learned_rules`.

The page must tolerate:

```text
RuleDisplayText = null
```

Use a fallback such as:

```text
rule-123
```

or another canonical rule ID.

Do not hide historical records simply because the active rule expired.

---

# 29. Rule Text Length

Do not display very long rule content unbounded in a table.

Use:

* Truncation
* CSS line clamp
* Details/expand interaction if the existing UI supports it

Always keep the canonical rule ID available.

---

# 30. Projects Page

Route:

```text
/statistics/projects
```

This page should provide cross-project operational comparison.

Recommended columns:

```text
Project
Reviews
Input Tokens
Output Tokens
Avg Review Time
Selection Rate
Error Rate
Active Rules
Avg Rule Search Time
```

Use `ProjectStatistics` from Phase 8.

---

# 31. Project Table Behavior

The table should be:

* Readable
* Responsive
* Deterministically ordered
* Usable with more than a handful of projects

If the current application already has table components, reuse them.

Do not introduce advanced client-side table libraries unless needed.

---

# 32. Avoid Misleading Rankings

The project page is a comparison view, not a scoring system.

Do not introduce labels such as:

```text
Best Project
Worst Project
Healthy
Unhealthy
```

unless those concepts already exist formally in the application.

Present measured statistics only.

---

# 33. Empty States

Each page must handle no data gracefully.

Examples:

```text
No reviews recorded in this period.
No learned-rule search data is available.
No rule usage has been recorded yet.
```

Do not render broken charts or empty HTML tables.

---

# 34. Partial Legacy Data

The UI must tolerate legacy records where some telemetry is missing.

Examples:

```text
ReviewExecution exists
ReviewTurn missing

Rule usage missing
Search telemetry missing
```

Display available data and use `—` for unavailable values.

Do not throw page-level errors because one metric is unavailable.

---

# 35. Service Failure Handling

If `IStatisticsService` throws due to a database/query failure:

* Follow the existing ASP.NET error-handling conventions.
* Avoid exposing SQL, stack traces, or internal schema details to users.
* Log the error through existing structured logging.
* Render a useful generic error state where appropriate.

Do not swallow failures silently.

---

# 36. Loading Behavior

For a normal SQLite-backed local dashboard, server-rendered pages are sufficient.

Do not add asynchronous client-side loading unless necessary.

Prefer:

```text
Request
   |
   v
PageModel/Controller
   |
   v
IStatisticsService
   |
   v
Rendered HTML
```

This keeps the implementation simple.

---

# 37. Responsive Layout

The dashboard should work on typical desktop widths and remain usable on narrower screens.

Recommended approach:

* CSS grid/flex for metric cards
* Horizontally scrollable tables if needed
* Charts that resize to container width
* Avoid fixed pixel page widths

Follow the existing application stylesheet.

---

# 38. Accessibility

Use semantic HTML.

Ensure:

* Tables have headers.
* Charts have visible titles.
* Color is not the only way to distinguish severity.
* Navigation can be used with keyboard.
* Important data remains available in text form even if charts fail.

Do not rely on chart tooltips as the only way to access metrics.

---

# 39. Severity Styling

Use restrained styling for:

```text
Critical
Major
Minor
```

Follow any existing severity color conventions.

If none exist, do not create overly aggressive visual styling.

Text labels must remain visible.

---

# 40. Security / Authorization

Inspect the existing application's authorization model.

If the application already restricts administrative/configuration pages, statistics pages should follow the appropriate existing policy.

Do not invent a new authentication system.

Do not expose dashboard pages publicly if the rest of the administrative UI is protected.

Document the chosen authorization behavior.

---

# 41. Project Names and URLs

Treat project names and repository URLs as untrusted display data.

Use Razor/MVC's normal HTML encoding.

Do not render repository metadata through raw HTML.

Avoid XSS vulnerabilities.

---

# 42. No Raw Review Content

The statistics dashboard should not display:

* Source code
* Full prompts
* Full model responses
* Merge request diff contents

unless the application already explicitly supports such a detail view.

Phase 9 is aggregate reporting.

---

# 43. No Full Error Messages

If error categories are displayed later, use stable categories such as:

```text
Timeout
ModelRequestFailed
ModelResponseInvalid
```

Do not expose stored exception payloads or stack traces.

---

# 44. Page Query Construction

Each page should create one consistent `StatisticsQuery`.

Example:

```csharp
var query = new StatisticsQuery(
    ProjectId: null,
    From: from,
    To: to);
```

Reuse that query for all service calls on the page where possible.

This prevents inconsistent date windows between cards and charts.

---

# 45. Current Phase Filter Scope

If Phase 10 is reserved for the full filter UI, Phase 9 may use a fixed default period and all-project view.

However, structure PageModels/controllers so that adding:

```text
projectId
from
to
```

query parameters in Phase 10 is straightforward.

Do not hardwire service calls deep into views.

---

# 46. Overview Page Service Calls

A reasonable implementation may call:

```csharp
GetOverviewAsync(...)
GetReviewTrendAsync(...)
GetTokenTrendAsync(...)
```

in parallel if the project's async/data-access conventions make this safe.

Do not complicate the implementation solely to optimize a few local SQLite reads.

Correctness and clarity come first.

---

# 47. Reviews Page Service Calls

Expected primary call:

```csharp
GetReviewStatisticsAsync(...)
```

and optionally:

```csharp
GetModelUsageAsync(...)
```

if model usage is exposed separately.

Do not query `review_turns` directly.

---

# 48. Rules Page Service Calls

Expected calls may include:

```csharp
GetRuleStatisticsAsync(...)
```

and, if Phase 8 separated the table query:

```csharp
GetRuleUsageAsync(...)
```

Reuse the actual Phase 8 API.

Do not modify service contracts unnecessarily solely for the page layout.

---

# 49. Projects Page Service Call

Expected:

```csharp
GetProjectStatisticsAsync(...)
```

This should supply the entire table.

Do not issue one service/database call per project from the web layer.

Avoid N+1 page rendering.

---

# 50. Chart Serialization

If charts need JSON embedded into Razor:

* Use the application's normal JSON serializer.
* Avoid manually concatenating JavaScript strings.
* HTML/JS encode safely.
* Keep data objects small and specific.

Do not serialize full service DTO graphs when only a few chart fields are needed.

---

# 51. JavaScript Scope

Keep dashboard JavaScript small.

Use JavaScript only for:

* Chart rendering
* Minor interaction
* Optional table enhancements

Do not reimplement server-side statistics formulas in JavaScript.

---

# 52. Progressive Enhancement

Core numbers and tables should still be visible if chart JavaScript fails.

Charts are supplementary visualizations.

The user should still see:

```text
Reviews
Tokens
Findings
Rule statistics
Project comparison
```

as normal HTML.

---

# 53. Styling

Follow existing site styles.

If no dashboard card system exists, add a small reusable statistics style set for:

```text
metric cards
section headings
chart containers
tables
empty states
```

Avoid creating a completely separate visual design language.

---

# 54. Suggested Overview Layout

A simple desktop layout could be:

```text
Statistics

[ Reviews ] [ Input Tokens ] [ Output Tokens ] [ Avg Review ]

[ Candidates ] [ Final Findings ] [ Selection Rate ] [ Error Rate ]

Reviews Over Time
------------------------------------------------

Token Usage Over Time
------------------------------------------------
```

Keep it readable and low-density.

---

# 55. Suggested Reviews Layout

```text
Review Statistics

Detection
[ Calls ] [ Input Tokens ] [ Output Tokens ] [ Avg Time ]

Selection
[ Calls ] [ Input Tokens ] [ Output Tokens ] [ Avg Time ]

Candidate -> Final
[ Candidates ] -> [ Final Findings ]    Selection Rate

Severity Distribution
Critical / Major / Minor

Model Usage
------------------------------------------------
```

---

# 56. Suggested Rules Layout

```text
Learned Rules

[ Active ] [ Created ] [ Expired ] [ Confidence + ] [ Confidence - ]

Rule Search
[ Avg Scanned ] [ Avg Candidates ] [ Avg Selected ] [ Avg Search Time ]

Rule Usage
--------------------------------------------------------------
Project | Rule | Prompt Uses | Candidate | Final | Hit Rates
```

---

# 57. Suggested Projects Layout

```text
Projects

Project | Reviews | Tokens | Avg Review | Selection | Errors | Rules | Search
--------------------------------------------------------------------------------
A       | ...
B       | ...
C       | ...
```

Keep sorting deterministic.

---

# 58. Testing Strategy

Add automated tests at the web layer without duplicating Phase 8 aggregation tests.

The goal is to verify:

* Routes work.
* Pages call the statistics service correctly.
* Values are rendered.
* Empty/null states are safe.
* No direct database access has leaked into the page layer.

---

# 59. Test 1 — Statistics Overview Route

Request:

```text
/statistics
```

Verify:

```text
HTTP success
```

and the page renders the expected overview sections.

---

# 60. Test 2 — Reviews Route

Request:

```text
/statistics/reviews
```

Verify:

```text
Detection
Selection
```

sections are rendered.

---

# 61. Test 3 — Rules Route

Request:

```text
/statistics/rules
```

Verify summary, search, and rule usage sections render.

---

# 62. Test 4 — Projects Route

Request:

```text
/statistics/projects
```

Verify project comparison rows are rendered from a mocked/test statistics service.

---

# 63. Test 5 — Overview Values

Provide a test `IStatisticsService` result with known values.

Verify the HTML contains the expected formatted values.

Do not test aggregation formulas here.

Those belong to Phase 8 tests.

---

# 64. Test 6 — Null Rate Rendering

Return:

```text
SelectionRate = null
ErrorRate = null
```

Verify the page renders a safe fallback such as:

```text
—
```

and does not display:

```text
NaN
Infinity
```

---

# 65. Test 7 — Empty Trends

Return empty trend arrays.

Verify chart sections render an empty state rather than failing.

---

# 66. Test 8 — Empty Rule Usage

Return zero rule usage rows.

Verify:

```text
No rule usage data...
```

or equivalent is rendered.

---

# 67. Test 9 — Expired Historical Rule

Return rule usage with:

```text
RuleDisplayText = null
RuleId = "rule-123"
```

Verify the page still renders a meaningful identifier.

---

# 68. Test 10 — Multiple Projects

Return multiple project rows.

Verify each appears exactly once.

---

# 69. Test 11 — HTML Encoding

Use a project name containing HTML-like characters.

Verify output is encoded and not rendered as arbitrary markup.

---

# 70. Test 12 — Authorization

If the statistics pages are protected by an existing authorization policy, add an integration test consistent with existing application tests.

Verify unauthorized users cannot access the pages where appropriate.

---

# 71. Test 13 — No Direct DB Dependency

Where the architecture/test setup allows it, verify PageModels/controllers depend on:

```text
IStatisticsService
```

rather than telemetry repositories or SQLite connection objects.

This can be enforced through code review if not easily automated.

---

# 72. Test 14 — Existing App Navigation

If adding a Statistics link to the existing navigation, verify the link points to the correct route and existing navigation remains intact.

---

# 73. Performance

The dashboard should remain lightweight.

Avoid:

* Loading raw telemetry into the page
* Returning thousands of rule rows by default
* Massive embedded JSON blobs
* Repeated calls for identical metrics
* Client-side aggregation

Use the compact aggregate DTOs from Phase 8.

---

# 74. Rule Table Size

If Phase 8 exposes a bounded rule list, honor that limit.

If it does not, consider adding a conservative display limit in cooperation with the existing Phase 8 contract.

Do not load an unbounded historical rule table into HTML if the dataset can become large.

If a limit is added, indicate that the table is limited.

Do not silently imply completeness.

---

# 75. Project Table Size

The project count is expected to be smaller than rule history.

A normal server-rendered table is sufficient initially.

Do not add pagination unless real project counts justify it.

---

# 76. Charts Must Not Block Page Rendering

If Chart.js or equivalent fails to initialize:

* Text metrics remain visible.
* Tables remain visible.
* The page remains usable.

Do not make the dashboard depend entirely on canvas output.

---

# 77. No Data Export Yet

Do not add:

```text
CSV export
JSON export
PDF reports
Excel export
```

in Phase 9.

These may be added later if useful.

---

# 78. No Auto Refresh

Do not add polling or real-time refresh.

The statistics pages can reflect the database state at request time.

Manual browser refresh is sufficient initially.

---

# 79. No WebSocket / SignalR Requirement

Do not introduce SignalR or live telemetry updates.

The usage statistics are not operationally time-critical enough to justify it in this phase.

---

# 80. No Daily Summary Tables

Continue using Phase 8 raw-telemetry aggregation.

Do not create dashboard-specific summary tables.

If page performance is poor, report the measured issue instead of silently introducing Phase 13 work.

---

# 81. No New Metric Definitions

The web layer must not invent new statistics such as:

```text
Quality Score
Rule Score
Project Health Score
Efficiency Score
```

unless they already exist in `IStatisticsService`.

Phase 9 presents trusted metrics; it does not define new ones.

---

# 82. Do Not Recompute Rates in Razor

Avoid:

```csharp
@((selected / candidates) * 100)
```

if Phase 8 already provides:

```text
SelectionRate
```

Likewise for:

```text
ErrorRate
CandidateHitRate
FinalHitRate
```

Use service values.

---

# 83. Logging

Use existing request/error logging.

Do not log every rendered statistic.

Do not log:

* Full rule content
* Source code
* Prompts
* Embeddings

The dashboard is read-only.

---

# 84. Dependency Injection

Register any new PageModel/controller dependencies through the existing DI setup.

Reuse the Phase 8 `IStatisticsService` registration.

Do not create ad hoc service locators or static accessors.

---

# 85. Cancellation

Pass request cancellation tokens into statistics service calls where the current ASP.NET style supports it.

For example:

```csharp
HttpContext.RequestAborted
```

or PageModel/controller cancellation parameters.

Do not intentionally ignore request cancellation for long-running reporting queries.

---

# 86. Error Boundaries for Charts

Invalid or missing chart data must not break the full page.

Validate chart inputs before serialization.

For no data, show a text empty state instead of constructing an invalid chart.

---

# 87. Future Phase 10 Compatibility

Structure pages so Phase 10 can add:

```text
Project
Period
From
To
```

filters without rewriting the whole dashboard.

A page model should already have one place where it creates:

```csharp
StatisticsQuery
```

Do not scatter date-range construction across sections.

---

# 88. Recommended Page Structure

A useful organization may be:

```text
Statistics/
    Index
    Reviews
    Rules
    Projects
```

for Razor Pages, or equivalent MVC controller/view structure.

If partial views/components are already standard, reuse them for:

```text
MetricCard
StatisticsNavigation
EmptyState
```

Do not introduce unnecessary component abstractions.

---

# 89. Expected Architecture After Phase 9

The target architecture becomes:

```text
Review / Learning Pipelines
          |
          v
        SQLite
          |
          v
   IStatisticsService
          |
          v
  ASP.NET Statistics UI
      /      |       \
     /       |        \
Overview   Reviews    Rules
     \
      \
     Projects
```

More explicitly:

```text
/statistics
        |
        +-- OverviewStatistics
        +-- ReviewTrend
        +-- TokenTrend

/statistics/reviews
        |
        +-- ReviewStatistics
        +-- ModelUsage

/statistics/rules
        |
        +-- RuleStatistics
        +-- RuleUsageStatistics

/statistics/projects
        |
        +-- ProjectStatistics
```

---

# 90. Relationship to Phase 10

Phase 10 will add richer filtering and navigation semantics, especially:

```text
All Projects
Project A
Project B
...

Last 7 Days
Last 30 Days
Custom Range
```

Phase 9 should therefore keep query construction centralized and avoid assumptions that every page will always display all projects.

Do not begin the full Phase 10 filter implementation unless a minimal default period is needed to render Phase 9.

---

# 91. Out of Scope

Do not implement:

* Full project filter UI
* Custom date-range picker
* Saved dashboard filters
* CSV export
* Excel export
* PDF export
* Live refresh
* SignalR
* Telemetry retention
* Daily summary/materialized tables
* Cost/billing dashboard
* Developer acceptance/rejection analytics
* Automated rule quality scoring
* Rule-confidence changes from dashboard metrics
* Vector-search optimization
* External analytics platform integration

Do not begin Phase 10.

---

# 92. Implementation Order

Use the following order unless the existing application structure strongly suggests a small adjustment.

```text
1. Inspect existing ASP.NET UI architecture
2. Inspect layout/navigation conventions
3. Inspect authorization conventions
4. Confirm Phase 8 IStatisticsService APIs
5. Add Statistics navigation/routes
6. Add overview PageModel/controller
7. Add overview summary cards
8. Add overview trend charts
9. Add Reviews page
10. Add Detection/Selection sections
11. Add severity/model usage display
12. Add Rules page
13. Add rule lifecycle summary
14. Add rule-search performance display
15. Add rule usage table
16. Add Projects page
17. Add project comparison table
18. Add shared formatting helpers
19. Add empty/null states
20. Add responsive styling
21. Add chart integration if needed
22. Add web-layer automated tests
23. Run existing tests
24. Manually inspect dashboard pages
25. Fix regressions without expanding scope
```

---

# 93. Acceptance Criteria

Phase 9 is complete when all of the following are true:

* `/statistics` exists.
* `/statistics/reviews` exists.
* `/statistics/rules` exists.
* `/statistics/projects` exists.
* Statistics navigation is available.
* All pages consume `IStatisticsService`.
* No statistics SQL exists in controllers/PageModels/views.
* Overview cards render trusted Phase 8 metrics.
* Review trend is visualized.
* Token trend is visualized.
* Detection and Selection are shown separately.
* Candidate-to-final selection information is shown.
* Critical/Major/Minor distribution is shown.
* Model usage is displayed if exposed by Phase 8.
* Active rule count is displayed.
* Rule lifecycle event counts are displayed.
* Rule-search performance is displayed.
* Rule usage/effectiveness metrics are displayed.
* Expired historical rules can still appear meaningfully.
* Multi-project comparison is displayed.
* Null/undefined rates are handled safely.
* Empty datasets render useful empty states.
* Legacy partial telemetry does not break pages.
* HTML output is safely encoded.
* Existing authorization conventions are applied.
* Charts are supplementary and do not hide the underlying metrics.
* Responsive layout is usable.
* Existing application behavior remains unchanged.
* Automated web tests pass.
* Existing full test suite passes.
* No Phase 10+ filtering functionality is unnecessarily introduced.

---

# 94. Coding Guidelines

Keep the dashboard server-rendered, simple, and read-only.

Follow existing:

* ASP.NET patterns
* Razor/MVC conventions
* Layout/navigation conventions
* Authorization policies
* Dependency injection
* CSS conventions
* JavaScript conventions
* Testing infrastructure

Keep presentation logic in the web layer and aggregation logic in Phase 8.

Do not bypass `IStatisticsService`.

Avoid unrelated refactoring.

The core principle is:

> Phase 9 should make the telemetry understandable to humans without changing how any metric is defined or collected.

---

# 95. Final Deliverable

After implementation, provide a concise implementation report containing:

1. Files changed.
2. Statistics routes/pages added.
3. ASP.NET UI approach used: Razor Pages or MVC.
4. How `IStatisticsService` is consumed.
5. Default reporting period used.
6. Overview metrics displayed.
7. Charts added and chart library used.
8. Review page sections implemented.
9. Rule page sections implemented.
10. Project comparison columns implemented.
11. Null/empty-state handling.
12. Expired-rule display behavior.
13. Authorization behavior.
14. Responsive/accessibility considerations.
15. Automated tests added or updated.
16. Any dashboard query/performance issues observed.
17. Any assumptions or follow-up items relevant to Phase 10.

Do not begin Phase 10 implementation.
