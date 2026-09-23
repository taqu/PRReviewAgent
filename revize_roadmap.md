## 改修ロードマップ

### Phase 1 — Turn 1をnon-thinking向けに軽量化

最初はアーキテクチャを変えず、`review1.en.md` の責務だけを狭めます。

現在のTurn 1は「候補発見」だけでなく、かなり強いevidence構築まで要求しています。たとえば発生条件、caller/callee behavior、impact、suggested fixまで自己完結させています。

これをnon-thinkingモデル向けに、

```text
Turn 1 = Candidate Discovery

- changed codeを読む
- concrete suspicionを列挙
- location
- root cause hypothesis
- verificationに必要なsymbol/context
- 根拠となったchanged code
```

までにします。

出力イメージは、

```json
{
  "issues": [
    {
      "candidate_id": "c0",
      "location": "src/foo.cpp: Foo::Open",
      "category": "lifetime",
      "hypothesis": "returned pointer may outlive its owner",
      "trigger": "Foo::Open now returns resource_.get()",
      "verify_symbols": [
        "Foo::~Foo",
        "Foo::resource_",
        "direct callers of Foo::Open"
      ]
    }
  ]
}
```

くらいで十分です。

このPhaseでは `impact`、`suggested_fix`、詳細な最終evidenceをTurn 1から外します。

**目的はTurn 1のlatency削減です。**

---

### Phase 2 — ASTを「prompt情報」から「context selector」に移す

ここが今回の本命です。

現在のpromptはAST contextをモデルに見せ、そのASTを使って仮説を確認させる設計になっています。

ただ、ASTを内蔵しているなら、

```text
AST JSON
    ↓
LLMに理解させる
```

ではなく、

```text
AST
 ↓
必要なsource fragmentをシステム側で選択
 ↓
LLMにはC/C++コードを渡す
```

へ寄せます。

AST内部では最低限、以下を保持します。

```text
Changed symbol
Declaration / definition
Containing class / struct
Direct callers
Direct callees
Referenced fields
Referenced types
Inheritance / implementation
Header / source pair
```

ただし、全部をTurn 1 promptには出しません。

Turn 1には、

```text
Changed symbols:
- Foo::Open
- Foo::Close

Direct structural relations:
- Foo::Open -> Resource::Acquire
- Foo::Open reads Foo::resource_
```

程度の小さなsemantic indexだけ付けます。

AST treeや大量のJSONは削ります。

---

### Phase 3 — Verification Turnを新設

Template構成を、

```text
Templates
├── review1.en.md
├── review2.en.md
└── review2.ja.md
```

から、

```text
Templates
├── review1.en.md        # Detection
├── review2.en.md        # Verification
├── review3.en.md        # Finalization
└── review3.ja.md        # Finalization / Japanese
```

へ変更するのがおすすめです。

`review2.ja.md` は最終出力用なら `review3.ja.md` に移す形です。

新しいTurn 2の仕事は一つだけです。

> Turn 1のcandidateが実際に成立するか確認する。

例えば入力を、

```text
Candidate c0
Hypothesis:
Foo::Open may return a pointer whose owner can be destroyed.

Changed code:
<relevant function>

AST-selected context:
<Foo declaration>
<Foo destructor>
<resource_ declaration>
<direct caller bodies>
```

とします。

出力はJSON。

```json
{
  "issues": [
    {
      "candidate_id": "c0",
      "valid": true,
      "evidence": "...",
      "impact": "...",
      "suggested_fix": "...",
      "confidence": "high"
    }
  ]
}
```

ここでもまだseverityを決めなくてよいです。

---

### Phase 4 — 現在のreview2をFinalization専用にする

現在の `review2.en.md` はすでにかなりFinalization向きです。

実際、

* candidate validation
* project policy
* false-positive rejection
* deduplication
* severity
* formatting

を担当しています。

ただしVerificationを別turnにした後は、ここから

```text
Validate each candidate
Evidence sufficient?
Execution conditions established?
```

といった重い検証責務を減らせます。

Turn 3は、

```text
verified candidates
        ↓
project-specific policy
        ↓
dedupe
        ↓
severity
        ↓
language / formatting
```

だけにします。

つまり今の `review2.en.md` を削るのではなく、**軽量化して `review3.en.md` に移植する**イメージです。

日本語版も、

```text
review3.en.md
review3.ja.md
```

だけ用意すればいいでしょう。

Detection / Verificationは内部処理なので英語固定で問題ありません。

---

## Phase 5 — Full-file contextへ移行

256Kコンテキストを使えるなら、changed fileについてはTurn 1で全文投入を基本にします。

ただし、

```text
[FILE: foo.cpp]

 unchanged
 unchanged
-removed
+added
 unchanged
```

のように変更位置を明示します。

Turn 1への情報は、

```text
MR title / description

File group

Changed-symbol summary

Full changed files with +/- markers

Very small related-symbol index
```

とします。

一方、**unchanged dependency filesを全文投入しない**のがポイントです。

それらはPhase 3のVerificationでASTにより必要部分だけ展開します。

---

## Phase 6 — Candidate-driven context expansion

ここでAST内蔵のメリットを最大化します。

Turn 1が、

```json
"verify_symbols": [
  "Foo::~Foo",
  "Foo::resource_",
  "direct callers of Foo::Open"
]
```

を返したら、システム側がそれを解釈して、

```text
Foo declaration
Foo::~Foo implementation
resource_ declaration
Foo::Open direct caller bodies
```

を取得します。

重要なのは、**モデルにAST検索を考えさせないこと**です。

理想は、

```text
Turn 1
「Foo::~Fooとcallerを確認すべき」
        ↓
AST Engine
必要コード抽出
        ↓
Turn 2
「このコードだけを使ってcandidateを確認」
```

です。

これならnon-thinkingモデルでもかなり安定します。

---

## Phase 7 — Execution pipelineを3-turn化

現在の実装は、

```text
BuildTurn1()
    ↓
IssuesResponse
    ↓
BuildTurn2()
    ↓
final review
```

です。DetectionとSelectionの境界がすでに明確です。 

これを、

```text
BuildTurn1()
    ↓
CandidateResponse

BuildVerificationContext()
    ↓
BuildTurn2()
    ↓
VerifiedIssuesResponse

BuildTurn3()
    ↓
Final review
```

にします。

型も分けた方がいいです。

```csharp
CandidateIssue
VerifiedIssue
FinalFinding
```

同じ`Issue`型を各stageで使い回さない方が安全です。

---

## Phase 8 — Recorder / metricsを更新

今はDetection / Selectionごとにturn recorderがあります。 

これを、

```text
Detection
Verification
Finalization
```

に変更します。

ここでは次のmetricsを必ず取った方がいいです。

```text
Turn1:
input tokens
output tokens
latency
candidate count

Turn2:
input tokens
output tokens
latency
verified count
rejected count

Turn3:
input tokens
output tokens
latency
final finding count

Overall:
total latency
total tokens
candidate -> verified ratio
verified -> final ratio
```

これがあると、

```text
thinking Turn1 + Turn2

vs

non-thinking Turn1 + Turn2 + Turn3
```

を数字で比較できます。

---

## Phase 9 — Grouping改善

これは3-turn化が安定してからでいいです。

現在はbase filenameによるgroupingです。

```text
foo.h
foo.cpp
→ foo
```

という方式なので、header/source pairingには非常に簡単で有効です。

ただASTがあるなら最終的には、

```text
directory/module affinity
+
header/source pair
+
changed-symbol relation
```

を使います。

例えば、

```text
include/render/image.h
src/render/image.cpp
src/render/image_loader.cpp
```

を一つのsemantic groupにできるようにします。

ただし、**latency短縮を目的とした最初の改修には入れない**方がいいです。

---

# 実装順序

私なら実際にはこの順で進めます。

1. **review1をCandidate Discovery専用に軽量化**
2. Turn1をthinkingなしにしてbenchmark
3. **review2 Verificationを新設**
4. 現review2をreview3 Finalizationへ分離
5. recorderを3-turn対応
6. Turn2用AST context extractorを実装
7. Turn1から大きなAST JSONを削除
8. changed file全文 + diff marker方式へ変更
9. benchmark
10. 最後にsemantic groupingを改善

この順なら各段階でrollbackできます。

---

## 最終アーキテクチャ

狙う形はこれです。

```text
                   GitLab MR
                       │
                       ▼
                 Fetch changes
                       │
                       ▼
               Full changed files
                 + diff markers
                       │
                       ▼
               AST / Semantic Index
                       │
              ┌────────┴────────┐
              │                 │
              ▼                 │
       Turn 1: Detection        │
        non-thinking            │
              │                 │
       Candidate hypotheses     │
              │                 │
              └──────┐          │
                     ▼          │
              Context Resolver ◄┘
                  (AST)
                     │
          targeted source context
                     │
                     ▼
          Turn 2: Verification
             non-thinking
                     │
             Verified issues
                     │
                     ▼
          Turn 3: Finalization
             non-thinking
                     │
          policy / severity /
           dedupe / language
                     │
                     ▼
              GitLab Review
```

この構成の肝は、**3-turn化そのものではなく、thinkingが担当していた探索・検証を「LLMの複数回推論 + deterministicなAST context resolution」に分解すること**です。

今の `review1.en.md` はすでに「changed codeからhypothesisを作り、ASTはその確認にだけ使う」という方向まで来ています。 なので、次の改修ではこの思想をもう一段進めて、**AST確認そのものをTurn 1からシステム側へ追い出す**のが自然です。

一番最初に着手するなら、**Phase 1〜4をひとまとまりの次Phase**にするのが良いと思います。ここだけで「thinking Turn1依存」からかなり脱却できます。
