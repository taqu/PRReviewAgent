
using System.Collections.Generic;

namespace PRReviewAget.Prompt;

// Language-specific rules that define which AST nodes are "meaningful" expansion boundaries.
// Compound/block node types (compound_statement, block, statement_block) are intentionally
// excluded so that control-flow nodes (if_statement, for_statement, …) are preferred —
// this ensures the expansion includes the condition/header, not just the body brace pair.

public interface IExpansionRules
{
    bool IsMeaningfulNode(string nodeType);
}

public sealed class CppExpansionRules : IExpansionRules
{
    public static readonly CppExpansionRules Instance = new();

    static readonly HashSet<string> _types = new(StringComparer.Ordinal)
    {
        "function_definition",
        "if_statement",
        "switch_statement",
        "case_statement",
        "for_statement",
        "while_statement",
        "do_statement",
        "try_statement",
        "catch_clause",
        "lambda_expression",
    };

    public bool IsMeaningfulNode(string nodeType) => _types.Contains(nodeType);
}

public sealed class CSharpExpansionRules : IExpansionRules
{
    public static readonly CSharpExpansionRules Instance = new();

    static readonly HashSet<string> _types = new(StringComparer.Ordinal)
    {
        "method_declaration",
        "constructor_declaration",
        "destructor_declaration",
        "operator_declaration",
        "conversion_operator_declaration",
        "local_function_statement",
        "if_statement",
        "switch_statement",
        "switch_section",
        "for_statement",
        "foreach_statement",
        "while_statement",
        "do_statement",
        "try_statement",
        "catch_clause",
        "lambda_expression",
        "anonymous_method_expression",
    };

    public bool IsMeaningfulNode(string nodeType) => _types.Contains(nodeType);
}

public sealed class RustExpansionRules : IExpansionRules
{
    public static readonly RustExpansionRules Instance = new();

    static readonly HashSet<string> _types = new(StringComparer.Ordinal)
    {
        "function_item",
        "if_expression",
        "match_expression",
        "match_arm",
        "for_expression",
        "while_expression",
        "loop_expression",
        "closure_expression",
        "impl_item",
    };

    public bool IsMeaningfulNode(string nodeType) => _types.Contains(nodeType);
}

public sealed class PythonExpansionRules : IExpansionRules
{
    public static readonly PythonExpansionRules Instance = new();

    static readonly HashSet<string> _types = new(StringComparer.Ordinal)
    {
        "function_definition",
        "class_definition",
        "if_statement",
        "for_statement",
        "while_statement",
        "try_statement",
        "except_clause",
        "with_statement",
    };

    public bool IsMeaningfulNode(string nodeType) => _types.Contains(nodeType);
}

public sealed class JsExpansionRules : IExpansionRules
{
    public static readonly JsExpansionRules Instance = new();

    static readonly HashSet<string> _types = new(StringComparer.Ordinal)
    {
        "function_declaration",
        "function",
        "arrow_function",
        "method_definition",
        "generator_function",
        "generator_function_declaration",
        "if_statement",
        "switch_statement",
        "switch_case",
        "for_statement",
        "for_in_statement",
        "for_of_statement",
        "while_statement",
        "do_statement",
        "try_statement",
        "catch_clause",
    };

    public bool IsMeaningfulNode(string nodeType) => _types.Contains(nodeType);
}

// Fallback rules when the language cannot be determined.
public sealed class GenericExpansionRules : IExpansionRules
{
    public static readonly GenericExpansionRules Instance = new();

    static readonly HashSet<string> _types = new(StringComparer.Ordinal)
    {
        "function_definition",
        "function_declaration",
        "function_item",
        "method_declaration",
        "method_definition",
        "if_statement",
        "for_statement",
        "while_statement",
    };

    public bool IsMeaningfulNode(string nodeType) => _types.Contains(nodeType);
}
