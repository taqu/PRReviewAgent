## Goal

最終目標は、

```text
旧 pipeline
Turn 1: thinking OFF
Turn 2: thinking OFF
```

のまま、

```text
品質 ≒ 旧 Turn 1 thinking ON
latency < thinking ON
false positive ≦ thinking ON
```

に持っていくことです。

今回の3件で言えば、最低ラインは、

```text
Environment::sample     検出
traceMesh               検出
renderSphere            検出
false positive          0
```

です。

thinking off が落としているのは `traceMesh` の `cross(T, normal)` だけなので、まずここを取り戻すのが最初のターゲットです。thinking on はこのbitangent反転を高confidenceで検出しています。

---

# Phase 0 — Baselineを固定する

まず旧実装を基準点として凍結します。

```text
Baseline-Q:
old pipeline
Turn1 thinking ON
Turn2 current
```

```text
Baseline-F:
old pipeline
Turn1 thinking OFF
Turn2 current
```

最低限、各benchmarkについて保存します。

```text
seeded bugs
detected true positives
false positives
Turn1 input tokens
Turn1 output tokens
Turn1 latency
Turn2 latency
total latency
```

今回のケースなら基準は、

```text
Baseline-Q
TP = 3
FN = 0

Baseline-F
TP = 2
FN = 1
```

です。

ここを以後絶対に動かさないようにします。

**目的:** 改修ごとの差分を正確に測れる状態にする。

---

# Phase 1 — thinking off用promptを「削らず」に整理する

ここは前回と逆です。

`impact` や `suggested_fix` を削りません。

むしろ旧promptの、

```text
problem
evidence
impact
suggested_fix
confidence
```

を維持します。

今回、thinking offでも `Environment::sample` と `renderSphere` について、かなり強い根拠を自力で構築できています。特に `renderSphere` では `renderMesh` の正しい実装との比較までできています。

なので、この構造を思考補助として残します。

ただしpromptの指示を少し強めます。

```text
For every changed expression, actively compare:

- width vs height
- row vs column
- lhs vs rhs
- old behavior vs new behavior
- sibling implementations
- inverse/order-sensitive operations
- ownership/lifetime symmetry
- initialization/cleanup symmetry
```

特にC/C++レビュー向けに、

```text
Pay special attention to:
- swapped dimensions
- reversed operands
- reversed cross-product order
- sign inversions
- off-by-one
- width/height confusion
- row/column confusion
```

を追加します。

今回の3バグは実はほぼ全部このカテゴリです。

**目的:** thinkingなしでも「比較軸」をprompt側から与える。

---

# Phase 2 — Diff-centric attentionを強化する

次はASTではなく、まず変更行を強調します。

入力を、

```text
full file
```

だけにせず、

```text
[CHANGED]
- const Vector3 B = cross(normal, T);
+ const Vector3 B = cross(T, normal);
[/CHANGED]
```

のように明示します。

もしくは、

```text
<<<< CHANGED
const Vector3 B = cross(T, normal);
>>>>
```

でも構いません。

重要なのは、non-thinkingモデルに、

> 「この行はレビュー対象の中心」

というattention hintを与えることです。

今回thinking offが `cross(T, normal)` を落とした理由の一つとして、変更点自体への注意が弱かった可能性があります。

Phase 2ではまだAST情報は追加しません。

```text
old prompt
+
old code context
+
explicit diff markers
```

だけです。

**Success condition:** 3/3が出るか確認。

---

# Phase 3 — Sibling implementation comparisonを自動追加する

今回の `renderSphere` がthinking offでも見つかった大きな理由は、

```text
renderSphere:
row * height

renderMesh:
row * width
```

という比較材料が同じcontext内にあったことです。

この「似たコードとの比較」をsystem側で意図的に作ります。

ASTまたはsemantic indexから、

```text
Changed function:
renderSphere

Similar / related functions:
renderMesh
```

を抽出して、

```text
Related implementation:
<renderMesh body>
```

をTurn 1に追加します。

ただし最初は高度な類似度計算は不要です。

候補は、

```text
same class
same call target
same constructor call
same field accesses
same return type
same naming prefix
```

程度で十分です。

例えば、

```text
renderSphere -> Sampler::Sampler
renderMesh   -> Sampler::Sampler
```

なら sibling として採用します。

**目的:** thinking onが自力で行っている「比較対象探索」をdeterministicに補助する。

---

# Phase 4 — Operator-sensitive semantic hintsを追加する

今回の最大の取りこぼしである、

```cpp
cross(T, normal)
```

は単純なsymbol relationだけでは検出しにくいです。

そこでsemantic indexに、単なる、

```text
traceMesh calls cross
```

ではなく、

```text
Changed expression:
cross(T, normal)

Argument semantic roles:
T      = tangent
normal = surface normal

Nearby related expression:
cross(normal, T)
```

のような情報を付けます。

特に以下の演算を対象にします。

```text
cross(a, b)
subtract(a, b)
divide(a, b)
comparison operands
matrix multiplication
quaternion multiplication
min/max
clamp
index arithmetic
```

これらは**引数順序に意味がある**ためです。

ここでASTは初めて本格的に使います。

ただしJSON treeはLLMに見せません。

LLMには、

```text
Semantic hints:
- cross() is called with tangent and normal
- this expression was changed by swapping argument order
```

程度を出します。

**目的:** thinkingが拾っていた「演算の意味」を低tokenで補う。

---

# Phase 5 — Changed-expression classificationを導入する

ここからかなり有効だと思います。

変更されたAST nodeをsystem側で分類します。

例えば今回なら、

```text
index % SampleHeight
→ dimension/index expression

cross(T, normal)
→ order-sensitive function call

row * settings.height
→ stride/index expression
```

と分類します。

Turn 1に、

```text
Changed expression categories:
- dimension arithmetic
- order-sensitive vector operation
- image indexing arithmetic
```

だけを渡します。

これならLLMが0から「何を見るべきか」を考える必要が減ります。

分類器は最初から賢くなくていいです。

```text
%, *, / + width/height
→ indexing/dimension

cross/dot/mul
→ vector/math operation

new/delete/free/reset
→ lifetime

lock/unlock
→ synchronization

size/count/length
→ bounds
```

くらいのheuristicでも十分です。

---

# Phase 6 — Review checklistを変更タイプ別に動的生成する

固定promptに全部書くのではなく、変更タイプによって短いchecklistを追加します。

例えばindexing changeなら、

```text
Check:
- Is row stride based on width?
- Is modulo based on width?
- Are width and height used consistently?
```

vector operationなら、

```text
Check:
- Is operand order significant?
- Does the result preserve coordinate-system handedness?
- Does the basis remain consistent with neighboring code?
```

これをsystem側で生成します。

つまり、

```text
AST classification
      ↓
review heuristic selection
      ↓
tiny prompt hint
```

です。

これならtoken増加は非常に小さいです。

---

# Phase 7 — Confidenceをthinking offにも復活させる

今回、thinking offではconfidenceが消えています。

これは戻した方がいいです。

```json
"confidence": "high | medium | low"
```

を必須にします。

ただしconfidenceを単なる自己評価として使わず、

```text
high:
changed code itself establishes the bug

medium:
requires a plausible semantic assumption

low:
speculative or missing context
```

と定義します。

Turn 2では、

```text
high
→ 通常検証

medium
→ 厳しく検証

low
→ 原則棄却候補
```

と使えます。

これだけでもfalse positive制御に使えます。

---

# Phase 8 — Turn 2は大きく変えない

ここが重要です。

今の失敗からすると、Turn 2をVerification専用に再設計する必要はありません。

旧Turn 2は、

```text
candidate validation
dedupe
severity
formatting
```

をまとめてやっていて、少なくとも今回のthinking offでは2件とも正しく通しています。

なので当面、

```text
Turn 1を改善する
Turn 2は固定
```

です。

Turn 1が3/3になる前にTurn 2を触ると、原因切り分けが難しくなります。

---

# Phase 9 — Full changed fileを基本化する

ここでようやくfull-file contextを試します。

順序としては、

```text
Diff
+
changed function
+
関連関数
```

より、

```text
full changed file
+
diff markers
```

の方が精度が高いか比較します。

256K contextが使えるなら、C/C++ではかなり有望です。

今回もthinking offは、同一ファイル内の `renderMesh` と `renderSphere` を比較してバグを発見しています。

なので、

```text
changed file全文
```

はむしろsemantic signalとして役立つ可能性があります。

ただし巨大ファイル用に、

```text
if file <= budget:
    full file
else:
    changed regions + related symbols
```

とします。

---

# Phase 10 — AST Context Selectorを追加する

full changed fileだけで足りない場合にのみ、unchanged dependencyを展開します。

例えば、

```text
changed function
↓
referenced type
↓
field declaration
↓
callee
↓
caller
```

から必要部分だけ追加します。

ここでもモデルには、

```text
AST JSON
```

を見せません。

```text
Relevant declarations:
...

Related implementation:
...
```

としてコードそのものを出します。

優先順位は、

```text
1. same-file sibling implementation
2. declaration
3. referenced fields
4. direct callee
5. direct caller
6. base/derived implementation
```

くらいで良いと思います。

---

# Phase 11 — Miss-driven context expansion

ここは少し後でいいですが、有効です。

Turn 1が低confidenceしか出せなかった場合だけ、

```text
additional context pass
```

を挟みます。

ただし3-turn reviewにはしません。

あくまでTurn 1入力生成側で、

```text
Initial static context
      ↓
LLM
```

ではなく、

```text
Static context builder
      ↓
rich enough context
      ↓
one LLM call
```

を目指します。

理想はLLM呼び出し回数を増やさないことです。

---

# Phase 12 — Benchmark corpusを増やす

3件だけでは最終判断できません。

最低でもバグタイプ別に用意します。

```text
indexing
width/height swap
argument reversal
sign reversal
lifetime
null handling
bounds
error propagation
resource cleanup
locking
iterator invalidation
integer narrowing
overflow
API contract mismatch
```

各カテゴリ5〜10件くらい欲しいです。

目標は例えば、

```text
50 seeded bugs
```

程度。

ここで初めて、

```text
thinking ON vs optimized thinking OFF
```

を判断します。

---

# Phase 13 — Acceptance gate

thinking offを本番採用する条件を先に決めておきます。

例えば、

```text
Recall:
>= thinking ON - 2%

False positive:
<= thinking ON + 1 finding / 100 reviews

Latency:
<= 60% of thinking ON

Token cost:
<= thinking ON
```

などです。

品質差が大きいなら、

```text
thinking ONを残す
```

という判断でいいです。

latencyのためにレビュー品質を落とす必要はありません。

---

# 実装順序

実際にはこの順番が良いと思います。

```text
1. old thinking-on / off benchmark固定

2. thinking-off promptに
   comparison checklistを追加

3. diff marker強化

4. confidence復活

5. benchmark

6. sibling implementation抽出

7. benchmark

8. changed-expression classification

9. operation-specific review hints

10. benchmark

11. full changed file投入

12. benchmark

13. AST-selected related source追加

14. benchmark

15. corpus拡大

16. production判定
```

ポイントは**毎回1要素しか変えないこと**です。

---

# 最終的に狙う構成

```text
GitLab MR
   │
   ▼
Changed files
   │
   ├── diff markers
   │
   ├── changed-expression classifier
   │
   ├── sibling implementation finder
   │
   └── AST semantic index
   │
   ▼
Context Builder
   │
   ├── full changed file
   ├── related implementation
   ├── tiny semantic hints
   └── operation-specific checklist
   │
   ▼
Turn 1
OLD review logic
thinking OFF

problem
evidence
impact
suggested_fix
confidence
   │
   ▼
Turn 2
existing validation /
dedupe / severity /
Japanese formatting
   │
   ▼
GitLab Review
```

この構成なら、**thinkingそのものをpipelineで再実装しようとするのではなく、thinkingが見つけていた「比較対象」と「見るべき観点」をdeterministicに前処理する**ことになります。

今回の結果から見ると、これが一番筋が良いです。

特に優先度を付けるなら、最初に試すべき3つは **① diff marker強化、② sibling implementation提示、③ order-sensitive operation hint** です。

`Environment::sample` と `renderSphere` はthinking offでも既に取れているので、当面の勝負は **`cross(T, normal)` をthinkingなしで安定して拾わせられるか** です。そこが取れたら、かなり有望です。
