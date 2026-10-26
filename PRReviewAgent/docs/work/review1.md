# Code Review 改良まとめ

## 1. 今回の目的

当初の目的は、コードレビュー品質を維持しながら `thinking on` 依存を減らし、レビュー時間を短縮することだった。

特に、

```text
thinking on
→ 高精度だが非常に遅い

thinking off
→ 高速だが、検出の安定性と指示追従がやや弱い
```

という差をどう埋めるかを検証した。

---

# 2. 最初に試した3-stage化

当初はレビュー処理を、

```text
Detection
    ↓
Verification
    ↓
Finalization
```

へ分割する案を検討した。

狙いは、thinkingが暗黙に行っていた探索・検証を、

```text
複数のnon-thinking LLM call
+
deterministic AST context resolution
```

へ分解することだった。

しかし実験では、Turn 1を軽量なCandidate Discoveryに限定するとDetection品質が低下した。

特に、

```text
problem
evidence
impact
suggested_fix
```

まで生成させていた旧Turn 1の方が、non-thinkingでも問題発見性能が高かった。

このことから、

```text
evidence construction
impact reasoning
suggested fix generation
```

は単なる出力ではなく、Detection自体を助けるreasoning scaffoldとして機能している可能性が高いと判断した。

そのため3-stage化は撤回した。

---

# 3. 旧2-turn pipelineをbaselineへ戻した

基本構成を、

```text
Turn 1
Detection + evidence construction

    ↓

Turn 2
validation / selection /
deduplication / severity /
final formatting
```

へ戻した。

Turn 1の出力制約は以下。

```text
Maximum 10 candidates

confidence must be high or medium

Do not output low-confidence candidates

Do not assign Critical, Major, or Minor severity

Do not use Markdown

Do not write the final review

Output JSON only
```

Turn 1は引き続き、

```text
location
problem
evidence
impact
suggested_fix
confidence
```

を生成する。

---

# 4. thinking on / off のbaseline比較

10個の意図的な変更を `src/renderer.cpp` に入れて比較した。

対象:

```text
1. MIS power heuristic
2. mirrored U coordinate
3. reversed atan2 arguments
4. SampleHeight / SampleWidth mismatch
5. incorrect UV interpolation
6. roughness U/V swap
7. reversed cross-product order
8. wrong lighting direction
9. row * height pixel seed
10. camera vertical coordinate / width
```

Baseline result:

```text
                             thinking off   thinking on

MIS heuristic                    2/10          6/10
mirrored U                      10/10          2/10
atan2 reversal                   0/10          0/10
SampleHeight modulo              7/10          8/10
UV interpolation                10/10         10/10
roughness UV swap                0/10          1/10
cross(T, normal)                 0/10          0/10
wrong lighting direction         9/10         10/10
row * height                     1/10          2/10
camera / width                   9/10          9/10
```

Timing:

```text
thinking off
172,620 ms

thinking on
1,684,855 ms
```

`thinking on` は約9.8倍遅かった。

一方、Detection性能は単純に `thinking on > thinking off` ではなかった。

一部はthinking onで改善したが、

```text
mirrored U:
thinking off 10/10
thinking on   2/10
```

のようにthinking offの方が大幅に安定するケースもあった。

そのため、

```text
thinking on = quality ceiling
```

とは扱わないことにした。

最終的に、

```text
thinking off
= production baseline

thinking on
= expensive reference / comparison mode
```

とした。

---

# 5. 「検出されない = model failure」ではない

10個の変更のうち、一部はコードだけではバグと断定できない。

代表例:

```text
atan2 argument order
roughness U/V convention
cross-product handedness
```

これらは、

```text
coordinate convention
texture convention
handedness
project-specific specification
```

に依存する。

したがって、

```text
seeded changeを指摘しなかった
=
false negative
```

とは必ずしも扱わない。

今後は、

```text
code-provable correctness issues

specification-dependent changes

policy / instruction adherence
```

を分けて評価する。

---

# 6. 以前 "false positive" と呼んでいたもの

Baseline thinking offでは、一部runで例えば、

```cpp
uint32_t width, height;
uint32_t size = width * height;
```

について、

```text
理論上uint32_t overflowが起きる
```

といった指摘が出た。

これは理論的には間違いではない。

しかしpromptでは、

```text
現実的に発生しない極端な条件
実用上actionableでない問題
理論上だけ成立する指摘
```

を除外するよう明示していた。

したがって、これは一般的なfalse positiveではなく、

```text
policy violation
```

または、

```text
instruction-following failure
```

として扱う方が正確。

Baseline:

```text
thinking off:
policy violation 3/10

thinking on:
policy violation 0/10
```

後のPhaseではthinking offでも0/10まで改善した。

つまり改善したのは単純なprecisionだけではなく、

```text
thinking offでdegradeしていた
prompt / review policyへの追従性能
```

でもある。

---

# 7. Semantic Grouping

thinkingを増やす代わりに、モデルへ与えるcontext構成を改善する方向へ変更した。

C/C++では、

```text
header/source pair
same containing class / struct
direct changed-symbol relationships
```

などを利用してsemantic groupingする。

C/C++以外はPhase 1では複雑化せず、

```text
one changed file
=
one review group
```

とした。

理由は、今回解決したい主問題が、

```text
C/C++の宣言/実装分離
複数translation unit間の関連
```

だから。

---

# 8. Group Budget

Semantic groupingによる巨大context化を防ぐため、

```text
group token budget
```

を導入した。

基本原則:

```text
strong semantic relationship
を優先して維持

weak relationship
から切る
```

優先順位の概念:

```text
same class / struct
header/source pair
direct changed-symbol relation
shared meaningful callee
base filename affinity
module/directory affinity
```

---

# 9. Changed Region Coverage

Recovery Detection導入の準備として、diff内のchanged regionを明示的に追跡するようにした。

概念:

```text
ChangedRegion
- file
- source range
- containing symbol
- stable region ID
```

Turn 1 candidateをchanged regionへmappingする。

重要なのは、

```text
reported
unreported
```

という言葉を使うこと。

```text
unreported
!=
unreviewed
!=
bug
```

Turn 1が見た上で問題なしと判断した可能性はシステム側では分からない。

---

# 10. Recovery Detection

thinkingを使う代わりに、もう1回 `thinking off` Detectionを行う方式を導入した。

構成:

```text
Primary Detection
thinking off

    ↓

Coverage Resolver

    ↓

Recovery Detection
thinking off

    ↓

Candidate Union

    ↓

Existing Turn 2
```

以前失敗した、

```text
Detection
→ Verification
→ Finalization
```

とは異なる。

Recoveryは候補を減らさない。

目的は、

```text
Primaryでcandidateが出なかった変更へ
もう一度attentionを割り当てる
```

こと。

---

# 11. Focused Recovery Context

RecoveryへPrimaryと同じcontextを再投入するのではなく、

```text
unreported changed regions
+
必要なlocal source context
+
限定されたstrong semantic supporting context
```

だけを渡すようにした。

考え方:

```text
Primary
= broad attention

Recovery
= focused attention
```

すでにPrimaryで報告された変更はRecoveryの主要ターゲットから除外する。

ただし、同じfunction内でcontextとして必要ならsource自体は残せる。

---

# 12. Recovery Prompt

Recovery専用promptでは、

```text
remaining regionは必ずしもbugではない
```

ことを明示した。

またnon-thinkingが安定して見るべき観点として、限定的に、

```text
dimension/index mismatch
same-type value swap
order-sensitive operation
wrong variable selection
sign/direction reversal
related implementation inconsistency
```

などを提示した。

ただし、

```text
仕様が分からないと断定できないもの
```

は無理に指摘させない。

Recallを上げるためにspec-dependent issueを強制的に拾わせる方向にはしなかった。

---

# 13. Conditional Recovery

Recoveryは常時実行するのではなく、

```text
Never
Always
Auto
```

を選択可能にした。

Autoでは、

```text
remaining changed work
```

を基準にRecoveryを発火する。

重要なのは、

```text
bug probability
```

を推測しないこと。

Recovery policyは、

```text
unreported region count
unreported changed lines
usable Recovery context
```

などのstructural informationだけで判断する。

---

# 14. Phase 7結果

Phase 1とPhase 7を10-runで比較。

```text
                             Phase 1    Phase 7

MIS heuristic                  4/10       7/10
mirrored U                    10/10      10/10
atan2 reversal                 0/10       0/10
SampleHeight modulo           10/10      10/10
UV interpolation              10/10      10/10
roughness UV swap              0/10       0/10
cross(T, normal)               0/10       0/10
wrong lighting direction       8/10       8/10
row * height                   2/10       1/10
camera / width                10/10      10/10

policy violation               0/10       0/10
```

Timing:

```text
Phase 1
204,183 ms

Phase 7
246,251 ms
```

Original:

```text
thinking off
172,620 ms

thinking on
1,684,855 ms
```

Phase 7は、

```text
original thinking off
の約1.43倍
```

だが、

```text
thinking on
の約1/6.8
```

程度。

---

# 15. Phase 7時点での評価

今回のbenchmarkでは、Phase 7はかなり良い結果になった。

主な改善:

```text
MIS heuristic
2/10 baseline
→ 7/10 Phase 7
```

安定していた問題:

```text
mirrored U
SampleHeight modulo
UV interpolation
camera / width
```

は10/10を維持。

また、

```text
policy violation
3/10
→ 0/10
```

となった。

一方、

```text
row * height
```

は依然として1/10程度で、Recoveryを追加しても改善していない。

したがって、

```text
さらにDetection passを増やせば解決する
```

とは考えない。

今後調査するとすれば、

```text
context composition
comparison target availability
change salience
```

など別要因として扱う。

---

# 16. 現時点の推奨アーキテクチャ

今回のリリースでは以下を完成形候補とする。

```text
GitLab MR
    ↓
Changed Files
    ↓
Language Policy
    │
    ├─ C/C++
    │    ↓
    │ Semantic Grouping
    │    ↓
    │ Budgeted Group Splitting
    │
    └─ Other Languages
         ↓
       Per-File Grouping

    ↓

Primary Detection
thinking off

    ↓

Changed-Region Coverage

    ↓

Recovery Execution Policy
    │
    ├─ skip
    │
    └─ run
         ↓
       Focused Recovery Context
         ↓
       Recovery Detection
       thinking off

    ↓

Candidate Union

    ↓

Existing Turn 2
validation
dedupe
severity
formatting

    ↓

GitLab Review
```

---

# 17. 今回の重要な知見

## 17.1 thinking時間を増やすことが単純な品質向上ではない

今回のbenchmarkでは、

```text
thinking on
```

は約10倍遅かったが、全カテゴリでthinking offを上回ったわけではない。

したがって、

```text
longer reasoning
=
uniformly better reviewer
```

とは仮定しない。

---

## 17.2 Context engineeringの効果が大きい

今回有効だった方向は、

```text
モデルをもっと考えさせる
```

より、

```text
何を一緒に見せるか
何を2回目に残すか
どこへattentionを割り当てるか
```

をシステム側で制御することだった。

---

## 17.3 Multiple cheap passesは有効

高価なthinking 1回より、

```text
Primary non-thinking
+
focused Recovery non-thinking
```

の方が、今回のbenchmarkでは実用的だった。

ただしRecoveryは同じ入力の単純retryではなく、

```text
remaining changed codeへattentionを再配分する
```

ことが重要。

---

## 17.4 DetectionとVerificationを細かく分割しすぎない

Turn 1から、

```text
evidence
impact
suggested_fix
```

を削るとDetection性能が悪化した。

non-thinkingでも、

```text
問題を発見
↓
なぜ問題か説明
↓
どう壊れるか考える
↓
最小修正を考える
```

まで一度に行わせることがDetectionを助けている。

---

## 17.5 `unreported` はfailureではない

Recovery architectureでは、

```text
unreported region
```

を、

```text
missed bug
```

と扱ってはいけない。

単に、

```text
Primary candidateと対応付かなかったchanged region
```

という事実だけを意味する。

これによりRecoveryのFP誘発を抑える。

---

## 17.6 Review policy adherenceは独立した品質軸

今回の、

```text
現実には成立しないoverflow指摘
```

のようなケースは単純なFPではない。

```text
promptで除外した指摘を出した
```

というinstruction-following failure。

今後のbenchmarkでは、

```text
Detection quality
Policy adherence
Specification-dependent behavior
```

を分けて評価する。

---

# 18. Phase 8

Phase 8ではreview logicを変更せず、benchmark instrumentationを完成させる。

記録対象:

```text
Primary:
duration
tokens
candidate count
reported/unreported region count

Recovery:
executed/skipped
decision reason
duration
tokens
candidate count
newly reported region count
duplicate count

Finalization:
duration
tokens
input candidate count
final finding count

Overall:
total latency
total tokens
Recovery execution rate
```

目的は、

```text
Recoveryに追加で何秒払ったか

その結果、
何candidate増えたか
何region追加で報告されたか
```

を定量化すること。

TP/FP/FNの自動判定は入れない。

---

# 19. 次期改善時に最初に確認すること

次の改良を始める際は、まずPhase 8データから以下を見る。

```text
1. Primaryだけで何が取れているか

2. Recoveryが実際に何を追加しているか

3. Recovery candidateのduplicate率

4. Recoveryを実行しても追加candidateが出ないgroupの割合

5. Auto policyがAlwaysに対して
   どれだけlatencyを削減できているか

6. Recoveryによって追加された候補が
   Turn 2でどれだけ残っているか
```

これを確認してから次の設計を決める。

---

# 20. 次期改善で避けること

現時点では以下を安易に追加しない。

```text
thinking on常用

Detection passの3回目追加

recursive Recovery

Verification専用LLM turnの復活

巨大AST JSONをpromptへ投入

repository-wide contextの無制限展開

spec-dependent issueを無理に拾わせるprompt

複雑なbug-risk classifier
```

実データで必要性が確認されてから追加する。

---

# 21. 今回のリリースの結論

今回のリリースでは、

```text
thinking off
+
semantic context organization
+
changed-region coverage
+
focused conditional Recovery
```

という方向が有効だった。

特に重要なのは、

```text
thinkingをシステム側で完全に再現した
```

のではなく、

```text
高価なreasoningに任せていたattention allocationの一部を
deterministicなpipelineへ移した
```

こと。

現時点ではPhase 7構成を一度固定し、Phase 8で計測を完成させた上で、次期改良は実測データから判断する。