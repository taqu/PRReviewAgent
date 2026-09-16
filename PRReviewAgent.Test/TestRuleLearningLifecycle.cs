using Microsoft.Data.Sqlite;
using PRReviewAgent.Services.AutoImprove;

namespace PRReviewAgent.Test;

[TestClass]
public class TestRuleLearningLifecycle
{
    private static async Task<(RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo)> CreateRepositoriesAsync()
    {
        string dbName = $"testdb_lifecycle_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";
        RuleRepository repo = new RuleRepository(cs, isConnectionString: true);
        ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
        RuleLearningEventRepository eventRepo = new RuleLearningEventRepository(cs, isConnectionString: true);
        await repo.InitializeAsync();
        return (repo, projRepo, eventRepo);
    }

    private static LearnedRule MakeRule(long projectId, string mergeRequestId, int confidence = 5, string description = "test rule")
    {
        return new LearnedRule
        {
            Id = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            MergeRequestId = mergeRequestId,
            AstPattern = "method_call",
            RuleDescription = description,
            BadPattern = "bad",
            GoodPattern = "good",
            Embedding = new float[] { 1.0f, 0.0f },
            ConfidenceScore = confidence,
            LastHitAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
    }

    // -------------------------------------------------------------------------
    // Test 1 — New rule creates a Created event
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task NewRule_CreatesCreatedEvent()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l1", "Project L1", null);
        LearnedRule rule = MakeRule(proj.Id, "mr-create-1", confidence: 5);

        await repo.InsertAsync(rule);

        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);

        Assert.AreEqual(1, events.Count);
        RuleLearningEvent e = events[0];
        Assert.AreEqual(RuleLearningEventType.Created, e.EventType);
        Assert.IsNull(e.ConfidenceBefore);
        Assert.AreEqual(5.0, e.ConfidenceAfter);
        Assert.AreEqual(proj.Id, e.ProjectId);
        Assert.AreEqual(rule.Id, e.RuleId);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Creation event stores the merge request ID
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task CreationEvent_StoresMergeRequestId()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l2", "Project L2", null);
        LearnedRule rule = MakeRule(proj.Id, "gitlab/99/7");

        await repo.InsertAsync(rule);

        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual("gitlab/99/7", events[0].MergeRequestId);
    }

    // -------------------------------------------------------------------------
    // Test 3 — Confidence increase creates ConfidenceIncreased event
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ConfidenceIncrement_CreatesEvent()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l3", "Project L3", null);
        LearnedRule rule = MakeRule(proj.Id, "mr-inc-3", confidence: 5);
        await repo.InsertAsync(rule);

        await repo.IncrementConfidenceByChunkIdAsync(proj.Id, rule.Id, "triggering-mr");

        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);
        RuleLearningEvent? inc = events.FirstOrDefault(e => e.EventType == RuleLearningEventType.ConfidenceIncreased);

        Assert.IsNotNull(inc);
        Assert.AreEqual(5.0, inc.ConfidenceBefore);
        Assert.AreEqual(6.0, inc.ConfidenceAfter);
        Assert.AreEqual("triggering-mr", inc.MergeRequestId);
    }

    // -------------------------------------------------------------------------
    // Test 4 — Confidence decrease creates ConfidenceDecreased event
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ConfidenceDecrement_CreatesEvent()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l4", "Project L4", null);
        LearnedRule rule = MakeRule(proj.Id, "mr-dec-4", confidence: 5);
        await repo.InsertAsync(rule);

        await repo.DecrementConfidenceByChunkIdAsync(proj.Id, rule.Id, "triggering-mr-dec");

        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);
        RuleLearningEvent? dec = events.FirstOrDefault(e => e.EventType == RuleLearningEventType.ConfidenceDecreased);

        Assert.IsNotNull(dec);
        Assert.AreEqual(5.0, dec.ConfidenceBefore);
        Assert.AreEqual(4.0, dec.ConfidenceAfter);
        Assert.AreEqual("triggering-mr-dec", dec.MergeRequestId);
    }

    // -------------------------------------------------------------------------
    // Test 5 — Active rule state and event confidence_after match after update
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task StateAndEventConfidenceMatch_AfterIncrement()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l5", "Project L5", null);
        LearnedRule rule = MakeRule(proj.Id, "mr-match-5", confidence: 7);
        await repo.InsertAsync(rule);

        await repo.IncrementConfidenceByChunkIdAsync(proj.Id, rule.Id);

        List<LearnedRule> active = await repo.GetAllActiveAsync(proj.Id);
        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);
        RuleLearningEvent? inc = events.LastOrDefault(e => e.EventType == RuleLearningEventType.ConfidenceIncreased);

        Assert.IsNotNull(inc);
        Assert.AreEqual((double)active[0].ConfidenceScore, inc.ConfidenceAfter);
    }

    // -------------------------------------------------------------------------
    // Test 6 — Project isolation: confidence change in A does not affect B's events
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ProjectIsolation_ConfidenceChanges()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:l6a", "Project L6A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:l6b", "Project L6B", null);

        LearnedRule ruleA = MakeRule(projA.Id, "shared-mr", confidence: 5);
        LearnedRule ruleB = MakeRule(projB.Id, "shared-mr", confidence: 5);
        await repo.InsertAsync(ruleA);
        await repo.InsertAsync(ruleB);

        await repo.IncrementConfidenceByChunkIdAsync(projA.Id, ruleA.Id);

        IReadOnlyList<RuleLearningEvent> eventsA = await eventRepo.GetByRuleAsync(projA.Id, ruleA.Id);
        IReadOnlyList<RuleLearningEvent> eventsB = await eventRepo.GetByRuleAsync(projB.Id, ruleB.Id);

        Assert.AreEqual(1, eventsA.Count(e => e.EventType == RuleLearningEventType.ConfidenceIncreased), "Project A should have a ConfidenceIncreased event");
        Assert.AreEqual(0, eventsB.Count(e => e.EventType == RuleLearningEventType.ConfidenceIncreased), "Project B should have no ConfidenceIncreased event");

        List<LearnedRule> rulesB = await repo.GetAllActiveAsync(projB.Id);
        Assert.AreEqual(5, rulesB[0].ConfidenceScore, "Project B confidence must remain unchanged");
    }

    // -------------------------------------------------------------------------
    // Test 7 — Expiration creates an Expired event
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Expiration_CreatesExpiredEvent()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l7", "Project L7", null);

        // Insert rule with old date directly via a helper insert with back-dated last_hit_at
        LearnedRule rule = MakeRule(proj.Id, "mr-expire-7", confidence: 1);
        rule.LastHitAt = DateTime.UtcNow.AddYears(-2);
        await repo.InsertAsync(rule);

        // Override the last_hit_at to be 2 years ago
        await repo.UpdateLastHitByMergeRequestIdAsync(proj.Id, "mr-expire-7", DateTime.UtcNow.AddYears(-2).ToString("O"));

        await repo.PruneStaleAsync();

        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);
        RuleLearningEvent? expired = events.FirstOrDefault(e => e.EventType == RuleLearningEventType.Expired);

        Assert.IsNotNull(expired, "An Expired event must be recorded");
        Assert.AreEqual(1.0, expired.ConfidenceBefore);
        Assert.IsNull(expired.ConfidenceAfter);
    }

    // -------------------------------------------------------------------------
    // Test 8 — Expired rule is removed from learned_rules but event persists
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ExpiredRule_RemovedFromActive_ButEventPersists()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l8", "Project L8", null);
        LearnedRule rule = MakeRule(proj.Id, "mr-expire-8", confidence: 1);
        await repo.InsertAsync(rule);
        await repo.UpdateLastHitByMergeRequestIdAsync(proj.Id, "mr-expire-8", DateTime.UtcNow.AddYears(-2).ToString("O"));

        await repo.PruneStaleAsync();

        List<LearnedRule> active = await repo.GetAllActiveAsync(proj.Id);
        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);

        Assert.AreEqual(0, active.Count, "Rule must be removed from learned_rules");
        Assert.IsTrue(events.Any(e => e.EventType == RuleLearningEventType.Expired), "Expired event must survive active-rule deletion");
    }

    // -------------------------------------------------------------------------
    // Test 9 — Expiration is multi-project safe: each event has correct project_id
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Expiration_IsMultiProjectSafe()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project projA = await projRepo.GetOrCreateAsync("ext:l9a", "L9A", null);
        Project projB = await projRepo.GetOrCreateAsync("ext:l9b", "L9B", null);

        LearnedRule ruleA = MakeRule(projA.Id, "mr-expire-9a", confidence: 1);
        LearnedRule ruleB = MakeRule(projB.Id, "mr-expire-9b", confidence: 1);
        await repo.InsertAsync(ruleA);
        await repo.InsertAsync(ruleB);
        await repo.UpdateLastHitByMergeRequestIdAsync(projA.Id, "mr-expire-9a", DateTime.UtcNow.AddYears(-2).ToString("O"));
        await repo.UpdateLastHitByMergeRequestIdAsync(projB.Id, "mr-expire-9b", DateTime.UtcNow.AddYears(-2).ToString("O"));

        await repo.PruneStaleAsync();

        IReadOnlyList<RuleLearningEvent> eventsA = await eventRepo.GetByRuleAsync(projA.Id, ruleA.Id);
        IReadOnlyList<RuleLearningEvent> eventsB = await eventRepo.GetByRuleAsync(projB.Id, ruleB.Id);

        Assert.IsTrue(eventsA.Any(e => e.EventType == RuleLearningEventType.Expired && e.ProjectId == projA.Id));
        Assert.IsTrue(eventsB.Any(e => e.EventType == RuleLearningEventType.Expired && e.ProjectId == projB.Id));
    }

    // -------------------------------------------------------------------------
    // Test 10 — Non-expired rule produces no Expired event
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task NonExpiredRule_ProducesNoExpiredEvent()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l10", "L10", null);
        LearnedRule rule = MakeRule(proj.Id, "mr-keep-10", confidence: 8);  // High confidence, recent
        await repo.InsertAsync(rule);

        await repo.PruneStaleAsync();

        List<LearnedRule> active = await repo.GetAllActiveAsync(proj.Id);
        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);

        Assert.AreEqual(1, active.Count, "Rule must remain active");
        Assert.AreEqual(0, events.Count(e => e.EventType == RuleLearningEventType.Expired), "No Expired event must be created");
    }

    // -------------------------------------------------------------------------
    // Test 11 — Duplicate rule creation does not duplicate the Created event
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task DuplicateRuleInsert_DoesNotDuplicateCreatedEvent()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l11", "L11", null);
        LearnedRule rule = MakeRule(proj.Id, "mr-dup-11", confidence: 5);

        await repo.InsertAsync(rule);
        await repo.InsertAsync(rule);  // duplicate — PK violation, should be silently ignored

        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);
        Assert.AreEqual(1, events.Count(e => e.EventType == RuleLearningEventType.Created), "Only one Created event should exist");
    }

    // -------------------------------------------------------------------------
    // Test 12 — Confidence update for nonexistent rule produces no event
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ConfidenceUpdate_ForNonexistentRule_ProducesNoEvent()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l12", "L12", null);
        string nonexistentId = Guid.NewGuid().ToString();

        await repo.IncrementConfidenceByChunkIdAsync(proj.Id, nonexistentId);

        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, nonexistentId);
        Assert.AreEqual(0, events.Count, "No event should be created for a nonexistent rule");
    }

    // -------------------------------------------------------------------------
    // Test 13 — Atomic transaction: state and event are both persisted
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task AtomicTransaction_StatAndEventBothPersisted()
    {
        (RuleRepository repo, ProjectRepository projRepo, RuleLearningEventRepository eventRepo) = await CreateRepositoriesAsync();
        Project proj = await projRepo.GetOrCreateAsync("ext:l13", "L13", null);
        LearnedRule rule = MakeRule(proj.Id, "mr-atomic-13", confidence: 5);
        await repo.InsertAsync(rule);

        await repo.IncrementConfidenceByChunkIdAsync(proj.Id, rule.Id);

        List<LearnedRule> active = await repo.GetAllActiveAsync(proj.Id);
        IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, rule.Id);
        RuleLearningEvent? inc = events.FirstOrDefault(e => e.EventType == RuleLearningEventType.ConfidenceIncreased);

        // Both the state update and the event must reflect the same transition
        Assert.IsNotNull(inc);
        Assert.AreEqual(6, active[0].ConfidenceScore);
        Assert.AreEqual(6.0, inc.ConfidenceAfter);
        Assert.AreEqual(5.0, inc.ConfidenceBefore);
    }

    // -------------------------------------------------------------------------
    // Test 14 — Existing pre-Phase-4 rules remain valid after migration
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task PrePhase4Rules_RemainValidAfterMigration()
    {
        // Simulate a pre-Phase-4 database (no rule_learning_events table) with an existing rule
        string dbName = $"testdb_lifecycle_premigrate_{Guid.NewGuid():N}";
        string cs = $"Data Source=file:{dbName}?mode=memory&cache=shared";

        // Set up old schema without rule_learning_events
        await using (SqliteConnection setupConn = new SqliteConnection(cs))
        {
            await setupConn.OpenAsync();
            await using SqliteCommand cmd = setupConn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE projects (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    external_project_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    display_name TEXT,
                    repository_url TEXT,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE UNIQUE INDEX ux_projects_external_project_id ON projects(external_project_id);
                INSERT INTO projects (external_project_id, name, is_active, created_at, updated_at)
                VALUES ('ext:premigrate', 'Old Project', 1, datetime('now'), datetime('now'));

                CREATE TABLE learned_rules (
                    id TEXT PRIMARY KEY,
                    project_id INTEGER NOT NULL,
                    merge_request_id TEXT NOT NULL,
                    ast_pattern TEXT NOT NULL,
                    rule_description TEXT NOT NULL,
                    bad_pattern TEXT,
                    good_pattern TEXT,
                    embedding BLOB NOT NULL,
                    confidence_score INTEGER NOT NULL DEFAULT 5,
                    last_hit_at TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                INSERT INTO learned_rules
                    (id, project_id, merge_request_id, ast_pattern, rule_description, embedding, confidence_score, last_hit_at, created_at)
                VALUES
                    ('pre-rule-1', 1, 'old-mr', 'pattern', 'Old rule', X'00000000', 8, datetime('now'), datetime('now'));";
            await cmd.ExecuteNonQueryAsync();

            // Run InitializeAsync against this old DB — it should add rule_learning_events without wiping data
            RuleRepository repo = new RuleRepository(cs, isConnectionString: true);
            await repo.InitializeAsync();

            // Verify the old rule survived
            ProjectRepository projRepo = new ProjectRepository(cs, isConnectionString: true);
            Project proj = await projRepo.GetOrCreateAsync("ext:premigrate", "Old Project", null);
            List<LearnedRule> rules = await repo.GetAllActiveAsync(proj.Id);

            Assert.AreEqual(1, rules.Count, "Pre-Phase-4 rule must survive migration");
            Assert.AreEqual("pre-rule-1", rules[0].Id);

            // Verify no fake Created events were generated
            RuleLearningEventRepository eventRepo = new RuleLearningEventRepository(cs, isConnectionString: true);
            IReadOnlyList<RuleLearningEvent> events = await eventRepo.GetByRuleAsync(proj.Id, "pre-rule-1");
            Assert.AreEqual(0, events.Count, "No synthetic Created events should be created for pre-Phase-4 rules");

            // Verify future confidence changes produce valid events
            await repo.IncrementConfidenceByChunkIdAsync(proj.Id, "pre-rule-1");
            IReadOnlyList<RuleLearningEvent> futureEvents = await eventRepo.GetByRuleAsync(proj.Id, "pre-rule-1");
            Assert.AreEqual(1, futureEvents.Count);
            Assert.AreEqual(RuleLearningEventType.ConfidenceIncreased, futureEvents[0].EventType);
        }
    }
}
