## Phase 1 — Rule Extraction を専用 SubAgent に分離

目的は、Code Review 用の Gemma 4 26B から rule extraction を切り離すことです。

```text
ReviewAgent
  └─ Gemma 4 26B

RuleExtractionSubAgent
  └─ Gemma 4 E4B
```

`RuleExtractionService` から現在の

```csharp
Context.Instance.Agents.RunAsync(...)
```

への直接依存を外し、`RuleExtractionSubAgent` 経由にします。現在はここでレビュー系 Agent と実行経路が共通化されています。

設定には例えば、

```toml
[subagent.rule_extraction]
enabled = true
endpoint = "..."
model = "gemma4-e4b"
max_output = 1024
temperature = 0.0
timeout = 120
```

を追加します。

`Settings` は起動時に一度ロードする設計なので、runtime 切替は不要です。起動時に設定を読み、そのモデルで `RuleExtractionSubAgent` を生成すれば十分です。

**この Phase の完成条件:**
26B を一切使わずに rule extraction が実行できる。

---

## Phase 2 — 多言語対応

現在の prompt は、

> `You are an expert static analysis bot for C/C++.`

と C/C++ 固定です。

これを language-neutral にします。

対象は既存設定に合わせて、

```text
C
C++
C#
Python
Rust
```

です。現在のデフォルト拡張子もこの範囲です。

例えば、

```csharp
enum SourceLanguage
{
    C,
    Cpp,
    CSharp,
    Python,
    Rust
}
```

を追加し、

```text
.c                  -> C
.cpp .cxx .cc
.hpp .inl           -> Cpp
.cs                 -> CSharp
.py                 -> Python
.rs                 -> Rust
```

とします。

`.h` は厳密な判定に凝らず、

* pair が `.c` なら C
* pair が `.cpp/.cc/.cxx` なら C++
* 判定不能なら C++

程度で十分です。

ここで重要なのは、**言語ごとに巨大な prompt を作らないこと**です。

共通 prompt に、

```text
Language: Rust
```

のような情報を追加し、必要なら短い language hint だけを付けます。

---

## Phase 3 — Expanded Diff を主入力にする

ここが精度面では最重要です。

現在は、

```text
File Dependencies
AST Context
Code Diff
```

をすべて prompt に入れています。

変更後は、

```text
Language
Expanded Diff
Relevant Structural Context
```

程度に絞ります。

つまり、

> **diff が主証拠、AST は補助証拠**

とします。

既に `AstContextExtractor.Run()` は expanded diff を返しているので、その資産はそのまま使えます。

**この Phase では AST 解析自体を削除しません。**

削るのは、

> full AST を SLM prompt にそのまま投入すること

だけです。

---

## Phase 4 — AST を conservative semantic slice に変更

ファイル全体の AST、全 symbol、全 include/import を E4B に渡すのはやめます。

代わりに changed code に直接関係する情報だけを選びます。

基本は、

```text
changed AST node
containing block
containing function/method
containing class/module
directly referenced symbols
added/removed imports/includes
```

まで。

必要なら、

```text
referenced type
direct callee signature
enum / trait / interface
```

を追加する程度です。

逆に通常は、

```text
full AST
all symbols
all imports/includes
whole dependency graph
unrelated callers/callees
```

を渡しません。

構造としては、

```text
Full file
   ↓
AST analysis
   ↓
relevance filtering
   ↓
compact context
   ↓
Gemma 4 E4B
```

です。

つまり方針は一言で、

> **解析は広く、SLMへの入力は狭く**

です。

---

## Phase 5 — Prompt を保守的な rule extraction に変更

今回の用途なら、モデルに「developer intent」や「project policy」を考えさせない方がいいです。

現在の、

```text
Extract the underlying engineering rule, coding standard,
or bug-fix pattern that the developer applied.
```

は少し広すぎます。

例えば次の方向にします。

```text
Extract the smallest reusable engineering rule directly demonstrated
by the Before/After change.

The changed code is the primary evidence.
Structural context is supporting evidence only.

Do not infer project-wide policy.
Do not infer intent that is not supported by the provided change.
Do not merely restate the textual diff.

If no clear reusable rule can be identified, return UNKNOWN.
```

出力は今の4項目を維持して構いません。

```json
{
  "ast_pattern": "...",
  "rule_description": "...",
  "bad_pattern": "...",
  "good_pattern": "..."
}
```

追加するなら `confidence` よりも、まず `UNKNOWN` を許可する方が効果があります。

今回の要件なら、

> **間違って学習するより、何も学習しない方が良い**

という設定で十分です。

---

## Phase 6 — 最小限の validation

ここも重くしなくていいです。

E4B の結果を保存する前に、単純な reject 条件だけ入れます。

例えば、

```text
rule_description == UNKNOWN
empty result
invalid JSON
bad_pattern == good_pattern
obviously generic rule
```

を捨てる。

generic 例：

```text
Follow best practices.
Handle errors properly.
Improve code quality.
```

現在は parse 成功後、そのまま `LearnedRule` と embedding を生成して保存しているので、この直前に薄い filter を1枚挟むだけで構いません。

