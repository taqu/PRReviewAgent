## 改良ロードマップ

### Phase 0 — Baseline固定

まず現行の改修前システムを固定します。

```text
Current grouping
↓
Turn 1: Detection
thinking off
↓
Turn 2: Selection / Finalization
thinking off
```

この状態を baseline とします。

保存する指標は、

```text
Turn 1 candidate count
Turn 1 TP / FP / FN
Turn 2 final TP / FP / FN

Turn 1 latency
Turn 2 latency
total latency

input/output tokens
```

です。

同じ10-bug benchmarkを複数runして、今後すべてこのbaselineと比較します。

---

### Phase 1 — Groupingをsemantic-awareに改善

最初にLLM pipelineは変えません。

現在のbase filename中心のgroupingを、

```text
header/source affinity
+
directory/module affinity
+
changed-symbol relation
```

へ拡張します。

例えば、

```text
Environment::sample
Environment::pdf
Environment::eval
```

や、

```text
renderSphere
renderMesh
```

のように、比較価値が高い実装を同じgroupへ寄せます。

優先順位は、

```text
1. Same class / struct
2. Header / source pair
3. Same directly referenced symbol
4. Same callee / constructor
5. Same module / directory
```

程度で十分です。

このPhaseではまだ、

```text
second detection pass
new prompt
AST context expansion
```

は入れません。

### 目的

**grouping変更だけで recall が上がるか確認する。**

---

### Phase 2 — Groupingサイズの制御

semantic groupingを入れるとgroupが巨大化する可能性があります。

そこでbudgetを入れます。

例えば、

```text
max files per group
max source lines
max estimated tokens
```

を設定します。

relationの弱いfileから切り離します。

概念的には、

```text
Strong relation
    same class
    same symbol
    direct caller/callee

Medium relation
    same module
    same header/source family

Weak relation
    directory proximity only
```

として、budget超過時にはWeakから削ります。

### 目的

semantic groupingによる精度向上を維持しつつ、latencyとcontext noiseを抑える。

---

### Phase 3 — Turn 1のcoverage情報を残す

second passを作る準備です。

Turn 1 candidateについて、

```text
location
changed symbol
changed region
```

をsystem側で対応付けられるようにします。

LLM出力schemaは無理に変えなくても構いません。

内部的に、

```text
ChangedRegionId
Candidate -> ChangedRegionId
```

を持てれば十分です。

最終的に、

```text
reviewed/reported changed regions
remaining changed regions
```

を判定できるようにします。

重要なのは、**「candidateが無かった = review済み」にはしない**ことです。

ここで管理するのは、

```text
already reported
```

だけです。

---

### Phase 4 — Recovery Detection Turnを追加

ここが本命です。

既存Turn 2を置き換えるのではなく、その前にもう一度Detectionを入れます。

```text
Semantic groups
      ↓
Turn 1A: Primary Detection
thinking off
      ↓
Primary candidates
      ↓
Turn 1B: Recovery Detection
thinking off
      ↓
Additional candidates
      ↓
Candidate union
      ↓
Existing Turn 2
Selection / Finalization
```

つまり以前試した、

```text
Detection
→ Verification
→ Finalization
```

ではありません。

今回は、

```text
Detection
→ Detection again
→ Finalization
```

です。

目的はcandidateを削ることではなく、**増やすこと**です。

---

### Phase 5 — Recovery Turnでは既出箇所を除外

Turn 1Bに同じ仕事をもう一度やらせないことが重要です。

入力を、

```text
Already reported:
- Environment::sample
- hitTriangle
- traceMesh line ...

Review the remaining changed code for additional issues.
Do not repeat already reported findings.
```

のようにします。

可能なら、既出changed region自体をcontextから削ります。

理想形は、

```text
Original semantic group
        ↓
remove already-reported regions
        ↓
Recovery context
```

です。

これにより、LLMのattentionを取りこぼした変更へ集中させます。

---

### Phase 6 — Recovery Turn専用promptを作る

Primary Detectionと同じpromptをそのまま再利用するより、Recovery専用に狭めます。

例えば責務は、

```text
Find additional correctness issues missed by the first pass.

Do not repeat existing findings.

Focus on remaining changed expressions.

Only emit high- or medium-confidence issues.
```

程度です。

ここでも、

```text
impact
evidence
suggested_fix
confidence
```

など既存candidate schemaは維持します。

新しいVerification schemaにはしません。

### 目的

新しい推論方式を作るのではなく、**attention allocationだけ変える**。

---

### Phase 7 — Candidate union / dedupe

Turn 1Aと1Bの候補をTurn 2へ渡す前に統合します。

最初は単純で構いません。

```text
same file
+
same function
+
same or overlapping line range
```

なら重複候補とみなします。

ただし自動的に強く削りすぎない方がいいです。

曖昧な場合はTurn 2に渡して、既存のdedupe能力を使います。

つまり、

```text
obvious duplicate → system dedupe
ambiguous duplicate → Turn 2
```

です。

---

### Phase 8 — Recovery Passの発火条件を最適化

最初は全レビューで2-passして構いません。

効果が確認できたらconditionalにします。

例えば、

```text
candidate_count < threshold
```

だけでは弱いです。

より良い条件は、

```text
unreported changed regions remain
AND
context budget allows second pass
```

です。

さらに将来的には、

```text
large number of changed expressions
multiple semantic groups
low coverage of changed regions
```

なども使えます。

ただし初期実装では無条件2-passの方がbenchmarkしやすいです。

---

### Phase 9 — Semantic group単位でRecoveryする

最初はMR全体でsecond passでもいいですが、最終的にはgroup単位の方がよいです。

```text
Group A
  Primary
  Recovery

Group B
  Primary
  Recovery
```

または、

```text
all groups Primary
↓
only weak-coverage groups Recovery
```

にします。

特に、

```text
groupに変更箇所が多い
candidateが少ない
```

ところだけRecoveryを掛けられるようになります。

---

### Phase 10 — Coverage metric追加

ここから重要なmetricを追加します。

```text
changed regions
reported regions
unreported regions

primary candidates
recovery candidates

recovery additional TP
recovery FP
recovery duplicate count
```

特に見るべきKPIは、

```text
Recovery Gain =
additional true positives from Turn 1B
```

です。

そして、

```text
Recovery Efficiency =
additional TP / additional latency
```

も有用です。

---

## Benchmark順序

各変更を混ぜないために、次の順番がいいです。

```text
A. Current baseline
   current grouping
   1 detection pass

B. Semantic grouping only
   improved grouping
   1 detection pass

C. Semantic grouping
   + 2 detection passes

D. Semantic grouping
   + remaining-region recovery context
```

ここまでで十分大きな判断ができます。

---

## 成功条件

今回の目的なら、absoluteな10/10を最初から要求しなくていいです。

例えば、

```text
Primary recall:
baseline以上

Recovery:
additional TP > 0

FP:
大幅に増加しない

Latency:
thinking onより十分短い
```

なら前進です。

thinking offが約173秒、thinking onが約1,685秒だったので、仮にDetectionを2回にして全体が2倍近くになっても、まだthinking onよりかなり短い余地があります。

目標としては、

```text
Quality:
thinking off 1-pass < thinking off 2-pass

Latency:
thinking off 2-pass << thinking on
```

をまず狙えば十分です。

---

## 最終アーキテクチャ候補

```text
GitLab MR
    │
    ▼
Changed files
    │
    ▼
AST / Semantic Relations
    │
    ▼
Semantic Group Builder
    │
    ▼
┌─────────────────────────┐
│ Turn 1A                 │
│ Primary Detection       │
│ thinking off            │
└────────────┬────────────┘
             │
      primary findings
             │
             ▼
     Coverage Resolver
             │
   remaining changed regions
             │
             ▼
┌─────────────────────────┐
│ Turn 1B                 │
│ Recovery Detection      │
│ thinking off            │
└────────────┬────────────┘
             │
      additional findings
             │
             ▼
       Candidate Union
             │
             ▼
┌─────────────────────────┐
│ Existing Turn 2         │
│ Selection / Dedupe      │
│ Severity / Formatting   │
│ thinking off            │
└────────────┬────────────┘
             │
             ▼
        GitLab Review
```

私なら実装順は、**Phase 1 semantic grouping → benchmark → Phase 4〜7 recovery detection → benchmark** の2つの大きなマイルストーンに分けます。これなら「groupingが効いたのか、second passが効いたのか」も綺麗に切り分けられます。
