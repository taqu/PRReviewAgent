
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TreeSitter;

namespace PRReviewAget.Prompt
{
    // =========================================================================
    // JSON Model
    // =========================================================================

    public class ImportInfo
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    public class ParameterInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("ref_kind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RefKind { get; set; }

        [JsonPropertyName("default_value")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DefaultValue { get; set; }
    }

    public class CallInfo
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = "";

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "method";
    }

    public class FunctionInfo
    {
        [JsonPropertyName("qualified_name")]
        public string QualifiedName { get; set; } = "";

        [JsonPropertyName("namespace")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Namespace { get; set; }

        [JsonPropertyName("containing_type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ContainingType { get; set; }

        [JsonPropertyName("visibility")]
        public string Visibility { get; set; } = "";

        [JsonPropertyName("static")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Static { get; set; }

        [JsonPropertyName("virtual")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Virtual { get; set; }

        [JsonPropertyName("override")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Override { get; set; }

        [JsonPropertyName("abstract")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Abstract { get; set; }

        [JsonPropertyName("final")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Final { get; set; }

        [JsonPropertyName("async")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Async { get; set; }

        [JsonPropertyName("constructor")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Constructor { get; set; }

        [JsonPropertyName("destructor")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Destructor { get; set; }

        [JsonPropertyName("operator")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Operator { get; set; }

        [JsonPropertyName("const")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool ConstMethod { get; set; }

        [JsonPropertyName("constexpr")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Constexpr { get; set; }

        [JsonPropertyName("noexcept")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Noexcept { get; set; }

        [JsonPropertyName("signature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Signature { get; set; }

        [JsonPropertyName("return_type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReturnType { get; set; }

        [JsonPropertyName("parameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ParameterInfo>? Parameters { get; set; }

        [JsonPropertyName("attributes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Attributes { get; set; }

        [JsonPropertyName("generic_parameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? GenericParameters { get; set; }

        [JsonPropertyName("start_line")]
        public int StartLine { get; set; }

        [JsonPropertyName("end_line")]
        public int EndLine { get; set; }

        [JsonPropertyName("change")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Change { get; set; }

        // Internal only — used to build the call graph, never serialized
        [JsonIgnore]
        public List<CallInfo>? Calls { get; set; }

        [JsonPropertyName("field_reads")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? FieldReads { get; set; }

        [JsonPropertyName("field_writes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? FieldWrites { get; set; }

        [JsonPropertyName("local_writes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? LocalWrites { get; set; }

        [JsonPropertyName("object_creations")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? ObjectCreations { get; set; }

        [JsonPropertyName("throws")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Throws { get; set; }

        [JsonPropertyName("awaits")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Awaits { get; set; }

        [JsonPropertyName("yields")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Yields { get; set; }

        [JsonPropertyName("locks")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Locks { get; set; }
    }

    public class TypeInfo
    {
        [JsonPropertyName("qualified_name")]
        public string QualifiedName { get; set; } = "";

        [JsonPropertyName("namespace")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Namespace { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        [JsonPropertyName("visibility")]
        public string Visibility { get; set; } = "";

        [JsonPropertyName("static")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Static { get; set; }

        [JsonPropertyName("abstract")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Abstract { get; set; }

        [JsonPropertyName("sealed")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Sealed { get; set; }

        [JsonPropertyName("base_class")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BaseClass { get; set; }

        [JsonPropertyName("interfaces")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Interfaces { get; set; }

        [JsonPropertyName("generic_parameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? GenericParameters { get; set; }

        [JsonPropertyName("attributes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Attributes { get; set; }

        [JsonPropertyName("start_line")]
        public int StartLine { get; set; }

        [JsonPropertyName("end_line")]
        public int EndLine { get; set; }
    }

    public class CallEdge
    {
        [JsonPropertyName("caller")]
        public string Caller { get; set; } = "";

        [JsonPropertyName("callee")]
        public string Callee { get; set; } = "";

        [JsonPropertyName("count")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Count { get; set; }
    }

    public class CounterpartContext
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = "";

        [JsonPropertyName("types")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TypeInfo>? Types { get; set; }

        [JsonPropertyName("functions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FunctionInfo>? Functions { get; set; }
    }

    public class StructuralDependencies
    {
        [JsonPropertyName("counterpart_context")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CounterpartContext? CounterpartContext { get; set; }
    }

    public class DiagnosticInfo
    {
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "warning";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("line")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Line { get; set; }
    }

    public class SummaryInfo
    {
        [JsonPropertyName("public_types")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? PublicTypes { get; set; }

        [JsonPropertyName("public_functions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? PublicFunctions { get; set; }

        [JsonPropertyName("dependencies")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Dependencies { get; set; }
    }

    public class OutputResult
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = "";

        [JsonPropertyName("summary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SummaryInfo? Summary { get; set; }

        [JsonPropertyName("imports")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ImportInfo>? Imports { get; set; }

        [JsonPropertyName("types")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<TypeInfo>? Types { get; set; }

        [JsonPropertyName("functions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FunctionInfo>? Functions { get; set; }

        [JsonPropertyName("call_graph")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<CallEdge>? CallGraph { get; set; }

        [JsonPropertyName("structural_dependencies")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public StructuralDependencies? StructuralDependencies { get; set; }

        [JsonPropertyName("diagnostics")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<DiagnosticInfo>? Diagnostics { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }
    }

    // =========================================================================
    // LanguageSupport
    // =========================================================================

    static class LanguageSupport
    {
        public static string GetOutputName(string filePath) =>
            Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".c" => "C",
                ".cpp" or ".cc" or ".h" or ".hpp" => "Cpp",
                ".cs" => "CSharp",
                ".rs" => "Rust",
                ".py" => "Python",
                ".js" => "JavaScript",
                ".ts" => "TypeScript",
                _ => "Unknown"
            };

        public static string GetTreeSitterName(string filePath) =>
            Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".c" => "C",
                ".cpp" or ".cc" or ".h" or ".hpp" => "Cpp",
                ".cs" => "C-Sharp",
                ".rs" => "Rust",
                ".py" => "Python",
                ".js" => "JavaScript",
                ".ts" => "TypeScript",
                _ => "Unknown"
            };

        public static bool IsImportNode(string lang, string type) => lang switch
        {
            "C" or "Cpp" => type == "preproc_include",
            "CSharp" => type == "using_directive",
            "Python" => type is "import_statement" or "import_from_statement",
            "JavaScript" or "TypeScript" => type == "import_statement",
            "Rust" => type == "use_declaration",
            _ => false
        };

        public static bool IsNamespaceNode(string lang, string type) => lang switch
        {
            "Cpp" => type == "namespace_definition",
            "CSharp" => type is "namespace_declaration" or "file_scoped_namespace_declaration",
            "Rust" => type == "mod_item",
            _ => false
        };

        public static bool IsTypeNode(string lang, string type) => lang switch
        {
            "C" or "Cpp" => type is "class_specifier" or "struct_specifier"
                                 or "union_specifier" or "enum_specifier",
            "CSharp" => type is "class_declaration" or "struct_declaration"
                             or "interface_declaration" or "enum_declaration"
                             or "record_declaration" or "record_struct_declaration",
            "Python" => type == "class_definition",
            "JavaScript" or "TypeScript" => type is "class_declaration"
                                              or "abstract_class_declaration"
                                              or "interface_declaration",
            "Rust" => type is "struct_item" or "enum_item" or "trait_item" or "union_item",
            _ => false
        };

        public static bool IsFunctionNode(string lang, string type) => lang switch
        {
            "C" or "Cpp" => type is "function_definition" or "declaration",
            "CSharp" => type is "method_declaration" or "constructor_declaration"
                             or "destructor_declaration" or "operator_declaration"
                             or "conversion_operator_declaration" or "local_function_statement",
            "Python" => type is "function_definition" or "decorated_definition",
            "JavaScript" or "TypeScript" => type is "function_declaration" or "method_definition"
                                              or "generator_function_declaration",
            "Rust" => type == "function_item",
            _ => false
        };

        public static bool IsFunctionBodyNode(string type) =>
            type is "block" or "compound_statement" or "statement_block";

        public static string GetImportKind(string lang) => lang switch
        {
            "C" or "Cpp" => "include",
            "CSharp" => "using",
            _ => "import"
        };

        public static string GetTypeKind(string type)
        {
            if (type.Contains("class")) return "class";
            if (type.Contains("struct")) return "struct";
            if (type.Contains("interface")) return "interface";
            if (type.Contains("enum")) return "enum";
            if (type.Contains("record")) return "record";
            if (type.Contains("union")) return "union";
            if (type.Contains("trait")) return "trait";
            return "type";
        }

        // C++ default member access per container type
        public static string CppDefaultAccess(string nodeType) =>
            nodeType == "class_specifier" ? "private" : "public";

        // Language-specific syntax-only callees that must not appear in the call graph.
        // These represent compile-time / parser constructs, not runtime dependencies.
        static readonly HashSet<string> _cppSyntaxOnlyCallees = new(StringComparer.Ordinal)
        {
            "static_cast", "reinterpret_cast", "const_cast", "dynamic_cast",
            "sizeof", "decltype", "typeid", "alignof", "__alignof__",
            "offsetof", "__builtin_offsetof",
        };

        public static bool IsSyntaxOnlyCallee(string lang, string name) =>
            lang is "C" or "Cpp" && _cppSyntaxOnlyCallees.Contains(name);

        // Heuristic: identifiers that look like member/field names per language.
        // C/C++: trailing underscore convention (width_, pixels_).
        // C#:    leading underscore convention (_width, _pixels).
        public static bool IsFieldIdentifier(string lang, string name) => lang switch
        {
            "C" or "Cpp" => name.Length > 1 && name[^1] == '_',
            "CSharp" => name.Length > 1 && name[0] == '_',
            _ => false
        };
    }

    // =========================================================================
    // DiffAnalyzer
    // =========================================================================

    static class DiffAnalyzer
    {
        public static bool IsNewFile(string diffText) =>
            diffText.Contains("--- /dev/null") || diffText.Contains("---\t/dev/null");

        public static HashSet<int> ParseModifiedLines(string? diffText)
        {
            HashSet<int> result = new HashSet<int>();
            if (string.IsNullOrEmpty(diffText)) return result;

            int lineNum = 0;
            bool inHunk = false;

            foreach (string line in diffText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("@@ "))
                {
                    int plus = line.IndexOf('+');
                    if (plus == -1) { inHunk = false; continue; }
                    int end = line.IndexOfAny(new[] { ',', ' ' }, plus + 1);
                    string startStr = end > plus + 1
                        ? line.Substring(plus + 1, end - plus - 1)
                        : line.Substring(plus + 1);
                    inHunk = int.TryParse(startStr, out lineNum);
                }
                else if (inHunk)
                {
                    if (line.StartsWith("+")) { result.Add(lineNum++); }
                    else if (line.StartsWith("-")) { /* removed — no new-file number */ }
                    else if (line.StartsWith(" ")) { lineNum++; }
                    else if (line.StartsWith("diff ") || line.StartsWith("index ")
                          || line.StartsWith("--- ") || line.StartsWith("+++ "))
                    {
                        inHunk = false;
                    }
                }
            }
            return result;
        }
    }

    // =========================================================================
    // WalkContext — state carried through the single-pass traversal
    // =========================================================================

    class WalkContext
    {
        public string Language { get; }
        public List<string> NamespaceStack { get; } = new();
        public List<string> TypeStack { get; } = new();
        // C++ only: one entry per type-nesting level tracking current access specifier
        public List<string> CppAccessStack { get; } = new();
        public List<ImportInfo> Imports { get; } = new();
        public List<TypeInfo> Types { get; } = new();
        public List<FunctionInfo> Functions { get; } = new();
        public List<string> PendingAttributes { get; } = new();

        public WalkContext(string language) => Language = language;

        public string CurrentNamespace =>
            NamespaceStack.Count > 0 ? string.Join(".", NamespaceStack) : "";

        public string CurrentType =>
            TypeStack.Count > 0 ? string.Join(".", TypeStack) : "";

        // Current C++ access specifier at the innermost type level
        public string CurrentCppAccess =>
            CppAccessStack.Count > 0 ? CppAccessStack[^1] : "public";

        public string BuildQualifiedName(string simpleName)
        {
            List<string> parts = new List<string>(3);
            string ns = CurrentNamespace;
            string ct = CurrentType;
            if (!string.IsNullOrEmpty(ns)) parts.Add(ns);
            if (!string.IsNullOrEmpty(ct)) parts.Add(ct);
            if (!string.IsNullOrEmpty(simpleName)) parts.Add(simpleName);
            return string.Join(".", parts);
        }
    }

    // =========================================================================
    // ImportCollector
    // =========================================================================

    static class ImportCollector
    {
        public static ImportInfo? TryCollect(Node node, string lang)
        {
            if (!LanguageSupport.IsImportNode(lang, node.Type)) return null;
            return new ImportInfo
            {
                Kind = LanguageSupport.GetImportKind(lang),
                Name = ExtractName(node, lang)
            };
        }

        static string ExtractName(Node node, string lang)
        {
            if (lang is "C" or "Cpp")
            {
                foreach (Node child in node.Children)
                    if (child.Type is "string_literal" or "system_lib_string")
                        return child.Text.Trim('<', '>', '"');
            }
            else if (lang == "Python" && node.Type == "import_from_statement")
            {
                string? fromPart = null;
                List<string> importedNames = new List<string>();
                bool pastImport = false;
                foreach (Node child in node.Children)
                {
                    if (child.Type == "from") continue;
                    if (child.Type == "import") { pastImport = true; continue; }
                    if (!pastImport) fromPart = child.Text;
                    else if (child.Type is "dotted_name" or "identifier" or "aliased_import")
                        importedNames.Add(child.Text.Split(' ')[0]);
                }
                if (fromPart != null && importedNames.Count == 1)
                    return $"{fromPart}.{importedNames[0]}";
                if (fromPart != null) return fromPart;
            }
            else
            {
                foreach (Node child in node.Children)
                {
                    if (child.Type is "identifier" or "qualified_name" or "dotted_name"
                        or "scoped_identifier" or "namespace_name")
                        return child.Text;
                    if (child.Type == "string")
                        return child.Text.Trim('"', '\'');
                }
            }
            return Regex.Replace(node.Text.Trim(), @"\s+", " ");
        }
    }

    // =========================================================================
    // TypeCollector
    // =========================================================================

    static class TypeCollector
    {
        public static TypeInfo? TryCollect(Node node, string lang, WalkContext ctx)
        {
            if (!LanguageSupport.IsTypeNode(lang, node.Type)) return null;

            string simpleName = FindSimpleName(node);
            if (string.IsNullOrEmpty(simpleName)) return null;

            TypeInfo info = new TypeInfo
            {
                QualifiedName = ctx.BuildQualifiedName(simpleName),
                Namespace = string.IsNullOrEmpty(ctx.CurrentNamespace) ? null : ctx.CurrentNamespace,
                Kind = LanguageSupport.GetTypeKind(node.Type),
                StartLine = node.StartPosition.Row + 1,
                EndLine = node.EndPosition.Row + 1,
            };

            CollectModifiers(node, lang, ctx, info);
            CollectGenericParameters(node, info);
            CollectBaseList(node, lang, info);

            if (ctx.PendingAttributes.Count > 0)
            {
                info.Attributes = new List<string>(ctx.PendingAttributes);
                ctx.PendingAttributes.Clear();
            }

            return info;
        }

        static string FindSimpleName(Node node)
        {
            foreach (Node child in node.Children)
                if (child.Type is "identifier" or "type_identifier" or "name")
                    return child.Text;
            return "";
        }

        static void CollectModifiers(Node node, string lang, WalkContext ctx, TypeInfo info)
        {
            List<string> vis = new List<string>();
            foreach (Node child in node.Children)
            {
                if (child.Type != "modifier") continue;
                switch (child.Text)
                {
                    case "public" or "private" or "protected" or "internal":
                        vis.Add(child.Text); break;
                    case "static": info.Static = true; break;
                    case "abstract": info.Abstract = true; break;
                    case "sealed": info.Sealed = true; break;
                }
            }

            if (lang is "C" or "Cpp")
                // Nested types in C++ inherit the current access specifier
                info.Visibility = ctx.TypeStack.Count > 0 ? ctx.CurrentCppAccess : "public";
            else
                info.Visibility = vis.Count > 0 ? string.Join(" ", vis) : "internal";
        }

        static void CollectGenericParameters(Node node, TypeInfo info)
        {
            foreach (Node child in node.Children)
            {
                if (child.Type is "type_parameter_list" or "type_parameters")
                {
                    List<string> gps = new List<string>();
                    foreach (Node gp in child.Children)
                        if (gp.Type is "type_parameter" or "identifier")
                            gps.Add(gp.Text);
                    if (gps.Count > 0) info.GenericParameters = gps;
                    return;
                }
            }
        }

        static void CollectBaseList(Node node, string lang, TypeInfo info)
        {
            foreach (Node child in node.Children)
            {
                if (child.Type is "base_list" or "class_heritage" or "superclasses"
                    or "implements_clause" or "extends_clause")
                {
                    List<string> interfaces = new List<string>();
                    bool firstSeen = false;
                    foreach (Node baseNode in child.Children)
                    {
                        if (baseNode.Type is "," or ":" or "extends" or "implements") continue;
                        string baseName = NormalizeTypeName(baseNode);
                        if (string.IsNullOrEmpty(baseName)) continue;

                        if (!firstSeen && lang is "CSharp" or "Cpp" or "C")
                        {
                            if (LooksLikeInterface(baseName))
                                interfaces.Add(baseName);
                            else
                                info.BaseClass = baseName;
                            firstSeen = true;
                        }
                        else
                        {
                            interfaces.Add(baseName);
                        }
                    }
                    if (interfaces.Count > 0) info.Interfaces = interfaces;
                    return;
                }
            }
        }

        static string NormalizeTypeName(Node node)
        {
            if (node.Type is "identifier" or "type_identifier" or "qualified_name"
                or "generic_name" or "scoped_type_identifier")
                return node.Text.Trim();
            foreach (Node child in node.Children)
            {
                string t = NormalizeTypeName(child);
                if (!string.IsNullOrEmpty(t)) return t;
            }
            return "";
        }

        static bool LooksLikeInterface(string name)
        {
            string simple = name.Split('<')[0].Split('.').Last();
            return simple.Length > 1 && simple[0] == 'I' && char.IsUpper(simple[1]);
        }
    }

    // =========================================================================
    // ReferenceCollector — analyses a function body (called once per function)
    // =========================================================================

    static class ReferenceCollector
    {
        sealed class State
        {
            public readonly List<CallInfo> Calls = new();
            public readonly HashSet<string> FieldReads = new();
            public readonly HashSet<string> FieldWrites = new();
            public readonly HashSet<string> LocalWrites = new();
            public readonly HashSet<string> ObjectCreations = new();
            public readonly HashSet<string> Throws = new();
            public readonly HashSet<string> Locks = new();
            public bool Awaits;
            public bool Yields;
        }

        public static void Analyze(Node body, string lang, FunctionInfo info)
        {
            State st = new State();
            Walk(body, lang, st);

            if (st.Calls.Count > 0) info.Calls = st.Calls;
            if (st.FieldReads.Count > 0) info.FieldReads = st.FieldReads.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
            if (st.FieldWrites.Count > 0) info.FieldWrites = st.FieldWrites.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
            if (st.LocalWrites.Count > 0) info.LocalWrites = st.LocalWrites.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
            if (st.ObjectCreations.Count > 0) info.ObjectCreations = st.ObjectCreations.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
            if (st.Throws.Count > 0) info.Throws = st.Throws.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
            if (st.Locks.Count > 0) info.Locks = st.Locks.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
            info.Awaits = st.Awaits;
            info.Yields = st.Yields;
        }

        static void Walk(Node node, string lang, State st)
        {
            switch (node.Type)
            {
                case "invocation_expression" or "call_expression":
                    {
                        string sym = ResolveCall(node);
                        if (!string.IsNullOrEmpty(sym) && !LanguageSupport.IsSyntaxOnlyCallee(lang, sym))
                            st.Calls.Add(new CallInfo { Symbol = sym, Kind = "method" });
                        // For C/C++: the method-call receiver is conservatively treated as a write
                        // (non-const method may mutate the object).
                        if (lang is "C" or "Cpp")
                        {
                            Node? callee = node.Children.Count > 0 ? node.Children[0] : null;
                            if (callee?.Type is "member_access_expression" or "member_expression"
                                or "field_expression" or "pointer_field_expression")
                            {
                                Node? recv = callee.Children.Count > 0 ? callee.Children[0] : null;
                                if (recv?.Type == "identifier"
                                    && LanguageSupport.IsFieldIdentifier(lang, recv.Text)
                                    && !string.IsNullOrEmpty(recv.Text))
                                {
                                    st.FieldWrites.Add(recv.Text);
                                }
                            }
                        }
                        // Walk arguments only, not the callee expression again
                        foreach (Node child in node.Children)
                            if (child.Type is "argument_list" or "arguments")
                                Walk(child, lang, st);
                        return;
                    }

                case "member_access_expression" or "member_expression"
                  or "field_expression" or "pointer_field_expression":
                    {
                        // this.Field or this->field read — record the field name
                        Node? obj = node.Children.Count > 0 ? node.Children[0] : null;
                        if (obj != null && (obj.Type is "this_expression" || obj.Text == "this"))
                        {
                            Node? field = null;
                            for (int i = node.Children.Count - 1; i >= 0; i--)
                            {
                                if (node.Children[i].Type is "identifier" or "property_identifier"
                                    or "field_identifier")
                                { field = node.Children[i]; break; }
                            }
                            if (field != null && !string.IsNullOrEmpty(field.Text))
                            {
                                st.FieldReads.Add(field.Text);
                            }
                        }
                        foreach (Node child in node.Children)
                            Walk(child, lang, st);
                        return;
                    }

                case "identifier" when lang is "C" or "Cpp":
                    // Bare identifiers with the trailing-underscore field naming convention
                    if (LanguageSupport.IsFieldIdentifier(lang, node.Text) && !string.IsNullOrEmpty(node.Text))
                    {
                        st.FieldReads.Add(node.Text);
                    }
                    return; // leaf node

                case "object_creation_expression" or "new_expression"
                  or "implicit_object_creation_expression":
                    {
                        string typeName = ExtractCreationType(node);
                        if (!string.IsNullOrEmpty(typeName))
                            st.ObjectCreations.Add(typeName);
                        foreach (Node child in node.Children)
                            Walk(child, lang, st);
                        return;
                    }

                case "throw_statement" or "throw_expression":
                    st.Throws.Add(ExtractThrowType(node));
                    return;

                case "await_expression":
                    st.Awaits = true;
                    break;

                case "yield_statement":
                    st.Yields = true;
                    break;

                case "lock_statement":
                    st.Locks.Add(ExtractLockTarget(node));
                    break;

                case "assignment_expression":
                    ClassifyAssignment(node, lang, st);
                    // Walk right-hand side for nested calls
                    if (node.Children.Count >= 3)
                        Walk(node.Children[2], lang, st);
                    return;
            }

            foreach (Node child in node.Children)
            {
                Walk(child, lang, st);
            }
        }

        static string ResolveCall(Node node)
        {
            Node? target = node.Children.Count > 0 ? node.Children[0] : null;
            if (target == null) return "";
            if (target.Type is "member_access_expression" or "member_expression"
                or "field_expression" or "pointer_field_expression")
                return BuildChain(target);
            if (target.Type is "identifier" or "property_identifier")
                return target.Text;
            return "";
        }

        static string BuildChain(Node node)
        {
            List<string> parts = new List<string>(4);
            CollectChain(node, parts);
            return string.Join(".", parts);
        }

        static void CollectChain(Node node, List<string> parts)
        {
            if (node.Type is "member_access_expression" or "member_expression")
            {
                foreach (Node child in node.Children)
                {
                    if (child.Type == ".") continue;
                    if (child.Type is "identifier" or "property_identifier"
                        or "type_identifier" or "generic_name")
                        parts.Add(child.Text);
                    else
                        CollectChain(child, parts);
                }
            }
            else if (node.Type is "identifier" or "type_identifier"
                  or "property_identifier" or "this_expression")
            {
                parts.Add(node.Text);
            }
        }

        static string ExtractCreationType(Node node)
        {
            foreach (Node child in node.Children)
                if (child.Type is "identifier" or "type_identifier"
                    or "qualified_name" or "generic_name" or "scoped_type_identifier")
                    return child.Text;
            return "";
        }

        static string ExtractThrowType(Node node)
        {
            foreach (Node child in node.Children)
            {
                if (child.Type is "object_creation_expression" or "new_expression")
                    return ExtractCreationType(child);
                if (child.Type is "identifier" or "type_identifier")
                    return child.Text;
            }
            return "Exception";
        }

        static string ExtractLockTarget(Node node)
        {
            foreach (Node child in node.Children)
                if (child.Type is "identifier" or "member_access_expression"
                    or "this_expression" or "parenthesized_expression")
                    return child.Type == "parenthesized_expression"
                        ? child.Children.FirstOrDefault(c => c.Type != "(" && c.Type != ")")?.Text ?? child.Text
                        : child.Text;
            return "";
        }

        static void ClassifyAssignment(Node node, string lang, State st)
        {
            Node? lhs = node.Children.Count > 0 ? node.Children[0] : null;
            if (lhs == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(lhs.Text))
            {
                return;
            }

            if (lhs.Type is "member_access_expression" or "element_access_expression"
                or "member_expression" or "field_expression" or "pointer_field_expression")
            {
                string chain = BuildChain(lhs);
                // Normalise: strip leading "this." so the entry is just the field name
                if (chain.StartsWith("this.") && chain.Length > 5)
                    chain = chain.Substring(5);
                st.FieldWrites.Add(chain);
            }
            else if (lhs.Type == "identifier" && !string.IsNullOrEmpty(lhs.Text))
            {
                if (LanguageSupport.IsFieldIdentifier(lang, lhs.Text))
                {
                    st.FieldWrites.Add(lhs.Text);
                }
                else
                {
                    st.LocalWrites.Add(lhs.Text);
                }
            }
        }
    }

    // =========================================================================
    // FunctionCollector
    // =========================================================================
    static class FunctionCollector
    {
        public static FunctionInfo? TryCollect(Node node, string lang, WalkContext ctx)
        {
            if (!LanguageSupport.IsFunctionNode(lang, node.Type)) return null;

            string simpleName = FindName(node, lang);
            bool isCtor = node.Type.Contains("constructor")
                        || (lang == "Python" && simpleName == "__init__");
            bool isDtor = node.Type.Contains("destructor")
                        || (lang is "C" or "Cpp" && simpleName.Contains("~"));
            bool isOp = node.Type.Contains("operator")
                        || (lang is "C" or "Cpp" && simpleName.Contains("operator"));

            FunctionInfo info = new FunctionInfo
            {
                QualifiedName = ctx.BuildQualifiedName(simpleName),
                Namespace = string.IsNullOrEmpty(ctx.CurrentNamespace) ? null : ctx.CurrentNamespace,
                ContainingType = string.IsNullOrEmpty(ctx.CurrentType) ? null : ctx.CurrentType,
                Constructor = isCtor,
                Destructor = isDtor,
                Operator = isOp,
                StartLine = node.StartPosition.Row + 1,
                EndLine = node.EndPosition.Row + 1,
            };

            CollectModifiers(node, lang, ctx, info);
            if (!isCtor && !isDtor) CollectReturnType(node, lang, info);
            CollectParameters(node, lang, info);
            CollectGenericParameters(node, info);

            if (lang is "C" or "Cpp")
                CollectCppQualifiers(node, info);

            if (ctx.PendingAttributes.Count > 0)
            {
                info.Attributes = new List<string>(ctx.PendingAttributes);
                ctx.PendingAttributes.Clear();
            }

            info.Signature = BuildSignature(info, lang);

            if (info.Parameters is { Count: 0 })
                info.Parameters = null;

            Node? body = FindBody(node);
            if (body != null)
                ReferenceCollector.Analyze(body, lang, info);

            return info;
        }

        static string FindName(Node node, string lang)
        {
            // Grammar field "name" is reliable for C# and most non-C++ languages.
            foreach (KeyValuePair<string?, Node> kv in node.Fields)
                if (kv.Key == "name") return kv.Value.Text;

            // C/C++ out-of-class definitions:
            //   function_definition
            //     [type_specifier]               ← return type (must NOT be used as the name)
            //     [pointer/reference_declarator] ← wraps function_declarator for T*/T& returns
            //       function_declarator
            //         qualified_identifier / operator_name / destructor_name  ← the actual name
            //     compound_statement
            //
            // Walk the declarator chain recursively so that both simple and
            // pointer/reference return types are handled uniformly.
            if (lang is "C" or "Cpp")
            {
                string name = FindCppFuncName(node);
                if (!string.IsNullOrEmpty(name)) return name;
            }

            // Fallback for all other languages: first identifier / property_identifier.
            // type_identifier is intentionally excluded — it is always a type reference.
            foreach (Node child in node.Children)
                if (child.Type is "identifier" or "property_identifier")
                    return child.Text;

            return "";
        }

        // Searches the direct children of `node` for a function_declarator,
        // recursing into pointer_declarator / reference_declarator wrappers.
        static string FindCppFuncName(Node node)
        {
            foreach (Node child in node.Children)
            {
                if (child.Type == "function_declarator")
                    return ExtractCppDeclName(child);

                if (child.Type is "pointer_declarator" or "reference_declarator"
                    or "abstract_pointer_declarator" or "abstract_reference_declarator")
                {
                    string name = FindCppFuncName(child);
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            return "";
        }

        // Extracts the callable name from a function_declarator node.
        // The name child is the first non-parameter, non-qualifier child and may be
        // a plain identifier, a qualified_identifier (Class::member), operator_name,
        // destructor_name, or itself wrapped in another pointer/reference declarator.
        static string ExtractCppDeclName(Node funcDecl)
        {
            foreach (Node dc in funcDecl.Children)
            {
                if (dc.Type is "identifier" or "qualified_identifier" or "scoped_identifier"
                    or "destructor_name" or "operator_name")
                    return dc.Text;

                if (dc.Type is "pointer_declarator" or "reference_declarator")
                {
                    string name = FindCppFuncName(dc);
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            return "";
        }

        static void CollectModifiers(Node node, string lang, WalkContext ctx, FunctionInfo info)
        {
            List<string> vis = new List<string>();

            foreach (Node child in node.Children)
            {
                if (lang is "C" or "Cpp")
                {
                    // C++ uses keyword nodes instead of generic "modifier"
                    switch (child.Type)
                    {
                        case "storage_class_specifier":
                            if (child.Text == "static") info.Static = true;
                            if (child.Text == "virtual") info.Virtual = true;
                            if (child.Text == "inline") { /* ignore */ }
                            if (child.Text == "explicit") { /* ignore */ }
                            break;
                        case "virtual":
                            info.Virtual = true;
                            break;
                        case "type_qualifier":
                            if (child.Text == "constexpr") info.Constexpr = true;
                            break;
                        case "virtual_specifier":
                            if (child.Text == "override") info.Override = true;
                            if (child.Text == "final") info.Final = true;
                            break;
                    }
                }
                else
                {
                    if (child.Type != "modifier") continue;
                    switch (child.Text)
                    {
                        case "public" or "private" or "protected" or "internal":
                            vis.Add(child.Text); break;
                        case "static": info.Static = true; break;
                        case "virtual": info.Virtual = true; break;
                        case "override": info.Override = true; break;
                        case "abstract": info.Abstract = true; break;
                        case "async": info.Async = true; break;
                        case "sealed": info.Final = true; break;
                    }
                }
            }

            if (lang is "C" or "Cpp")
            {
                // C++ visibility from enclosing class access specifier
                info.Visibility = ctx.TypeStack.Count > 0 ? ctx.CurrentCppAccess : "public";
            }
            else
            {
                info.Visibility = vis.Count > 0
                    ? string.Join(" ", vis)
                    : DefaultVisibility(lang);
            }
        }

        // C++ trailing qualifiers live on the function_declarator's children
        static void CollectCppQualifiers(Node node, FunctionInfo info)
        {
            foreach (Node child in node.Children)
            {
                if (child.Type == "compound_statement") continue;
                ScanQualifier(child, info);

                if (child.Type == "function_declarator")
                    foreach (Node dc in child.Children)
                        ScanQualifier(dc, info);
            }
        }

        static void ScanQualifier(Node node, FunctionInfo info)
        {
            switch (node.Type)
            {
                case "type_qualifier":
                    if (node.Text == "const") info.ConstMethod = true;
                    if (node.Text == "constexpr") info.Constexpr = true;
                    break;
                case "virtual_specifier":
                    if (node.Text == "final") info.Final = true;
                    if (node.Text == "override") info.Override = true;
                    break;
                case "noexcept":
                    info.Noexcept = true;
                    break;
                case "storage_class_specifier":
                    if (node.Text == "virtual") info.Virtual = true;
                    if (node.Text == "static") info.Static = true;
                    break;
            }
        }

        static string DefaultVisibility(string lang) => lang switch
        {
            "Python" or "JavaScript" or "TypeScript" => "public",
            _ => "private"
        };

        static void CollectReturnType(Node node, string lang, FunctionInfo info)
        {
            if (lang is "C" or "Cpp")
            {
                // Assemble C++ return type: [const] BaseType [*|&|&&]
                // type_qualifier and base type appear as direct children of function_definition;
                // the declarator wrapper (reference_declarator / pointer_declarator) indicates suffix.
                List<string> prefixes = new List<string>();
                string? baseType = null;
                string suffix = "";
                foreach (Node child in node.Children)
                {
                    if (child.Type == "type_qualifier")
                        prefixes.Add(child.Text);
                    else if (child.Type is "type_identifier" or "primitive_type"
                             or "scoped_type_identifier" or "template_type" or "auto")
                        baseType = child.Text;
                    else if (child.Type is "pointer_declarator" or "abstract_pointer_declarator")
                        suffix = "*";
                    else if (child.Type is "reference_declarator" or "abstract_reference_declarator")
                        suffix = GetRefSuffix(child);
                    else if (child.Type is "function_declarator" or "compound_statement")
                        break;
                }
                if (baseType != null)
                {
                    string rt = prefixes.Count > 0
                        ? string.Join(" ", prefixes) + " " + baseType + suffix
                        : baseType + suffix;
                    info.ReturnType = rt;
                }
                return;
            }

            // Try grammar field
            foreach (KeyValuePair<string?, Node> kv in node.Fields)
                if (kv.Key is "type" or "return_type")
                { info.ReturnType = kv.Value.Text; return; }

            // Fallback: first type-like child after modifiers
            bool pastModifiers = false;
            foreach (Node child in node.Children)
            {
                if (child.Type is "modifier" or "storage_class_specifier"
                    or "type_qualifier" or "virtual") { pastModifiers = true; continue; }
                if (!pastModifiers) continue;

                if (child.Type is "predefined_type" or "nullable_type" or "array_type"
                    or "generic_name" or "qualified_name" or "identifier" or "type_identifier"
                    or "void_keyword" or "tuple_type" or "pointer_type" or "ref_type"
                    or "type_specifier" or "primitive_type" or "scoped_type_identifier")
                {
                    info.ReturnType = child.Text;
                    return;
                }
                // Stop at parameter list / body
                if (child.Type is "parameter_list" or "formal_parameters" or "parameters"
                    or "type_parameter_list" or "block" or "compound_statement"
                    or "function_declarator")
                    return;
            }
        }

        static string GetRefSuffix(Node refDecl)
        {
            foreach (Node c in refDecl.Children)
                if (c.Text == "&&") return "&&";
            return "&";
        }

        static void CollectParameters(Node node, string lang, FunctionInfo info)
        {
            foreach (Node child in node.Children)
            {
                if (child.Type is "parameter_list" or "formal_parameters" or "parameters")
                {
                    List<ParameterInfo> parms = new List<ParameterInfo>();
                    foreach (Node param in child.Children)
                        if (param.Type is "parameter" or "required_parameter"
                            or "optional_parameter" or "variadic_parameter"
                            or "parameter_declaration")
                            parms.Add(ParseParameter(param, lang));
                    if (parms.Count > 0) info.Parameters = parms;
                    return;
                }
                // Recurse through declarator wrappers to find nested function_declarator
                if (child.Type is "function_declarator" or "pointer_declarator"
                    or "reference_declarator" or "rvalue_reference_declarator"
                    or "abstract_pointer_declarator" or "abstract_reference_declarator")
                    CollectParameters(child, lang, info);
            }
        }

        static ParameterInfo ParseParameter(Node param, string lang)
        {
            if (lang is "C" or "Cpp" && param.Type == "parameter_declaration")
                return ParseCppParameter(param);

            ParameterInfo p = new ParameterInfo();

            // Grammar field access (reliable for C#)
            foreach (KeyValuePair<string?, Node> kv in param.Fields)
            {
                switch (kv.Key)
                {
                    case "name": p.Name = kv.Value.Text; break;
                    case "type": p.Type = kv.Value.Text; break;
                    case "default_value": p.DefaultValue = kv.Value.Text; break;
                }
            }
            if (!string.IsNullOrEmpty(p.Name)) return p;

            // Fallback child scanning
            bool seenType = false;
            foreach (Node child in param.Children)
            {
                switch (child.Type)
                {
                    case "modifier":
                        if (child.Text is "ref" or "out" or "in" or "params")
                            p.RefKind = child.Text;
                        break;
                    case "predefined_type" or "type_identifier" or "nullable_type"
                      or "array_type" or "generic_name" or "qualified_name"
                      or "primitive_type" or "scoped_type_identifier":
                        if (!seenType) { p.Type = child.Text; seenType = true; }
                        break;
                    case "identifier":
                        if (seenType && string.IsNullOrEmpty(p.Name))
                            p.Name = child.Text;
                        else if (!seenType)
                        { p.Type = child.Text; seenType = true; }
                        break;
                    case "=": break;
                    default:
                        if (seenType && !string.IsNullOrEmpty(p.Name) && p.DefaultValue == null
                            && child.Type != ",")
                            p.DefaultValue = child.Text;
                        break;
                }
            }
            return p;
        }

        static ParameterInfo ParseCppParameter(Node param)
        {
            ParameterInfo p = new ParameterInfo();
            List<string> prefixes = new List<string>();
            string? baseType = null;

            foreach (Node child in param.Children)
            {
                switch (child.Type)
                {
                    case "type_qualifier":
                        prefixes.Add(child.Text);
                        break;
                    case "type_identifier" or "primitive_type" or "scoped_type_identifier"
                      or "template_type" or "auto":
                        baseType = child.Text;
                        break;
                    case "pointer_declarator":
                        p.Type = BuildCppBaseType(prefixes, baseType) + "*";
                        p.Name = FindInnermostIdentifier(child);
                        return p;
                    case "reference_declarator":
                        p.Type = BuildCppBaseType(prefixes, baseType) + GetRefSuffix(child);
                        p.Name = FindInnermostIdentifier(child);
                        return p;
                    case "rvalue_reference_declarator":
                        p.Type = BuildCppBaseType(prefixes, baseType) + "&&";
                        p.Name = FindInnermostIdentifier(child);
                        return p;
                    case "identifier":
                        if (baseType != null)
                            p.Name = child.Text;
                        break;
                }
            }

            if (baseType != null)
                p.Type = BuildCppBaseType(prefixes, baseType);
            return p;
        }

        static string BuildCppBaseType(List<string> prefixes, string? baseType)
        {
            if (baseType == null) return "";
            return prefixes.Count > 0
                ? string.Join(" ", prefixes) + " " + baseType
                : baseType;
        }

        static string FindInnermostIdentifier(Node node)
        {
            foreach (Node child in node.Children)
            {
                if (child.Type == "identifier") return child.Text;
                string found = FindInnermostIdentifier(child);
                if (!string.IsNullOrEmpty(found)) return found;
            }
            return "";
        }

        static void CollectGenericParameters(Node node, FunctionInfo info)
        {
            foreach (Node child in node.Children)
                if (child.Type is "type_parameter_list" or "type_parameters")
                {
                    List<string> gps = new List<string>();
                    foreach (Node gp in child.Children)
                        if (gp.Type is "type_parameter" or "identifier")
                            gps.Add(gp.Text);
                    if (gps.Count > 0) info.GenericParameters = gps;
                    return;
                }
        }

        static Node? FindBody(Node node)
        {
            foreach (Node child in node.Children)
                if (LanguageSupport.IsFunctionBodyNode(child.Type))
                    return child;
            return null;
        }

        static string BuildSignature(FunctionInfo info, string lang)
        {
            StringBuilder sb = new StringBuilder();

            // No return type for constructors or destructors
            if (!info.Constructor && !info.Destructor && !string.IsNullOrEmpty(info.ReturnType))
            {
                sb.Append(info.ReturnType);
                sb.Append(' ');
            }

            // For methods inside a type: prepend ContainingType:: (or . for non-C++)
            // Out-of-class C++ definitions already carry Class:: inside their simple name.
            string simpleName = info.QualifiedName.Split('.').Last();
            if (info.Destructor && !simpleName.Contains("~"))
                simpleName = "~" + simpleName;

            if (!string.IsNullOrEmpty(info.ContainingType))
            {
                string sep = lang is "C" or "Cpp" ? "::" : ".";
                string typePart = lang is "C" or "Cpp"
                    ? info.ContainingType.Replace(".", "::")
                    : info.ContainingType;
                sb.Append(typePart);
                sb.Append(sep);
            }
            sb.Append(simpleName);

            if (info.GenericParameters?.Count > 0)
                sb.Append($"<{string.Join(", ", info.GenericParameters)}>");

            sb.Append('(');
            if (info.Parameters?.Count > 0)
            {
                sb.Append(string.Join(", ", info.Parameters.Select(p =>
                {
                    List<string> parts = new List<string>(3);
                    if (!string.IsNullOrEmpty(p.RefKind)) parts.Add(p.RefKind);
                    if (!string.IsNullOrEmpty(p.Type)) parts.Add(p.Type);
                    if (!string.IsNullOrEmpty(p.Name)) parts.Add(p.Name);
                    return string.Join(" ", parts);
                })));
            }
            sb.Append(')');

            if (info.ConstMethod) sb.Append(" const");
            if (info.Noexcept) sb.Append(" noexcept");
            if (info.Final) sb.Append(" final");

            return sb.ToString().Trim();
        }

        internal static string CanonicalId(FunctionInfo info, string lang)
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(info.ContainingType))
            {
                string typePart = lang is "C" or "Cpp"
                    ? info.ContainingType.Replace(".", "::")
                    : info.ContainingType;
                sb.Append(typePart);
                sb.Append(lang is "C" or "Cpp" ? "::" : ".");
            }
            string simpleName = info.QualifiedName.Split('.').Last();
            sb.Append(simpleName);
            sb.Append('(');
            if (info.Parameters?.Count > 0)
                sb.Append(string.Join(", ", info.Parameters.Select(p => p.Type ?? "")));
            sb.Append(')');
            if (info.ConstMethod) sb.Append(" const");
            return sb.ToString();
        }
    }

    // =========================================================================
    // AstWalker — single-pass traversal dispatching to collectors
    // =========================================================================

    class AstWalker
    {
        readonly WalkContext _ctx;

        public AstWalker(WalkContext ctx) => _ctx = ctx;

        public void Walk(Node node)
        {
            string lang = _ctx.Language;

            // Track C++ access specifiers inside class bodies
            if (lang is "C" or "Cpp" && node.Type == "access_specifier")
            {
                string access = node.Text.TrimEnd(':').Trim().ToLowerInvariant();
                if (_ctx.CppAccessStack.Count > 0)
                    _ctx.CppAccessStack[^1] = access;
                return;
            }

            if (node.Type is "attribute_list" or "attribute_section" or "attribute")
            {
                _ctx.PendingAttributes.Add(node.Text.Trim());
                return;
            }

            if (LanguageSupport.IsImportNode(lang, node.Type))
            {
                ImportInfo? imp = ImportCollector.TryCollect(node, lang);
                if (imp != null) _ctx.Imports.Add(imp);
                return;
            }

            if (LanguageSupport.IsNamespaceNode(lang, node.Type))
            {
                string ns = ExtractNamespaceName(node);
                bool pushed = !string.IsNullOrEmpty(ns);
                if (pushed) _ctx.NamespaceStack.Add(ns);
                foreach (Node child in node.Children)
                    Walk(child);
                if (pushed) _ctx.NamespaceStack.RemoveAt(_ctx.NamespaceStack.Count - 1);
                return;
            }

            if (LanguageSupport.IsTypeNode(lang, node.Type))
            {
                // Push C++ default access before collecting type info
                if (lang is "C" or "Cpp")
                    _ctx.CppAccessStack.Add(LanguageSupport.CppDefaultAccess(node.Type));

                TypeInfo? typeInfo = TypeCollector.TryCollect(node, lang, _ctx);
                if (typeInfo != null) _ctx.Types.Add(typeInfo);

                string simpleName = typeInfo?.QualifiedName.Split('.').Last() ?? "";
                _ctx.TypeStack.Add(simpleName);

                foreach (Node child in node.Children)
                    if (!LanguageSupport.IsFunctionBodyNode(child.Type))
                        Walk(child);

                _ctx.TypeStack.RemoveAt(_ctx.TypeStack.Count - 1);
                if (lang is "C" or "Cpp" && _ctx.CppAccessStack.Count > 0)
                    _ctx.CppAccessStack.RemoveAt(_ctx.CppAccessStack.Count - 1);
                return;
            }

            if (LanguageSupport.IsFunctionNode(lang, node.Type))
            {
                FunctionInfo? fn = FunctionCollector.TryCollect(node, lang, _ctx);
                if (fn != null) _ctx.Functions.Add(fn);
                return;
            }

            foreach (Node child in node.Children)
                Walk(child);
        }

        string ExtractNamespaceName(Node node)
        {
            foreach (KeyValuePair<string?, Node> kv in node.Fields)
                if (kv.Key == "name") return kv.Value.Text;
            foreach (Node child in node.Children)
                if (child.Type is "identifier" or "qualified_name"
                    or "namespace_name" or "scoped_identifier")
                    return child.Text;
            return "";
        }
    }

    // =========================================================================
    // CallGraphCollector
    // =========================================================================

    static class CallGraphCollector
    {
        public static List<CallEdge> Build(List<FunctionInfo> functions, string lang)
        {
            Dictionary<(string caller, string callee), int> counts = new Dictionary<(string caller, string callee), int>();
            foreach (FunctionInfo fn in functions)
            {
                if (fn.Calls == null) continue;
                string caller = FunctionCollector.CanonicalId(fn, lang);
                foreach (CallInfo call in fn.Calls)
                {
                    if (string.IsNullOrEmpty(call.Symbol)) continue;
                    (string caller, string Symbol) key = (caller, call.Symbol);
                    counts.TryGetValue(key, out int c);
                    counts[key] = c + 1;
                }
            }
            return counts
                .Select(kv => new CallEdge
                {
                    Caller = kv.Key.caller,
                    Callee = kv.Key.callee,
                    Count = kv.Value > 1 ? kv.Value : (int?)null,
                })
                .ToList();
        }
    }

    // =========================================================================
    // SummaryBuilder
    // =========================================================================

    static class SummaryBuilder
    {
        public static SummaryInfo Build(WalkContext ctx, string filePath)
        {
            List<string> publicTypes = ctx.Types
                .Where(t => t.Visibility.Contains("public"))
                .Select(t => t.QualifiedName.Split('.').Last())
                .Distinct()
                .ToList();

            HashSet<string> publicTypeNames = ctx.Types
                .Where(t => t.Visibility.Contains("public"))
                .Select(t => t.QualifiedName.Split('.').Last())
                .ToHashSet(StringComparer.Ordinal);

            List<string> publicFunctions = ctx.Functions
                .Where(f => f.Visibility.Contains("public") &&
                            (f.ContainingType == null ||
                             publicTypeNames.Contains(f.ContainingType.Split('.').Last())))
                .Select(f => BuildPublicFunctionEntry(f, ctx.Language))
                .Distinct()
                .ToList();

            List<string> dependencies = ctx.Imports
                .Select(i => i.Name)
                .Distinct()
                .ToList();

            return new SummaryInfo
            {
                PublicTypes = publicTypes.Count > 0 ? publicTypes : null,
                PublicFunctions = publicFunctions.Count > 0 ? publicFunctions : null,
                Dependencies = dependencies.Count > 0 ? dependencies : null,
            };
        }

        static string BuildPublicFunctionEntry(FunctionInfo f, string lang) =>
            FunctionCollector.CanonicalId(f, lang);

    }

    // =========================================================================
    // AstContextExtractor — public API
    // =========================================================================

    public class AstContextExtractor
    {
        static readonly JsonSerializerOptions SerializerOptions = new()
        {
#if DEBUG
            WriteIndented = true,
#else
            WriteIndented = false,
#endif
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static (string json, string diff) Run(string filePath, string? fileContent, string? diffPath,
            string? diffContent, string? relatedPath, string? relatedContent)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(filePath));

            string lang = LanguageSupport.GetOutputName(filePath);
            string tsLang = LanguageSupport.GetTreeSitterName(filePath);

            fileContent ??= File.ReadAllText(filePath);

            using Language language = new Language(tsLang);
            using Parser parser = new Parser(language);
            using Tree tree = parser.Parse(fileContent)
                ?? throw new Exception("Failed to parse file");

            OutputResult result = new OutputResult { Language = lang };

            // Single-pass traversal
            WalkContext ctx = new WalkContext(lang);
            AstWalker walker = new AstWalker(ctx);
            walker.Walk(tree.RootNode);

            result.Summary = SummaryBuilder.Build(ctx, filePath);
            result.Imports = ctx.Imports.Count > 0 ? ctx.Imports : null;
            result.Types = ctx.Types.Count > 0 ? ctx.Types : null;
            result.Functions = ctx.Functions.Count > 0 ? ctx.Functions : null;

            List<CallEdge> callGraph = CallGraphCollector.Build(ctx.Functions, lang);
            result.CallGraph = callGraph.Count > 0 ? callGraph : null;

            // Diff analysis — classify functions as added / modified
            if (!string.IsNullOrEmpty(diffPath) && string.IsNullOrEmpty(diffContent)) {
                try { diffContent = File.ReadAllText(diffPath); } catch { }
            }
            string expandedDiff = diffContent;
            if (!string.IsNullOrEmpty(diffContent) && !string.IsNullOrEmpty(diffPath)) {
                AnnotateModifications(ctx.Functions, diffContent);
                expandedDiff = DiffExpander.ExpandDiff(tree, diffContent, diffPath);
                if(string.IsNullOrEmpty(expandedDiff)) {
                    expandedDiff = diffContent;
                }
            }

            // Structural dependencies — parse related file with its own parser
            if (!string.IsNullOrEmpty(relatedPath) && string.IsNullOrEmpty(relatedContent)) {
                try { relatedContent = File.ReadAllText(relatedPath); } catch { }
            }

            if (!string.IsNullOrEmpty(relatedContent))
            {
                string relLangName = !string.IsNullOrEmpty(relatedPath)
                    ? LanguageSupport.GetOutputName(relatedPath) : lang;
                string relTsName = !string.IsNullOrEmpty(relatedPath)
                    ? LanguageSupport.GetTreeSitterName(relatedPath) : tsLang;

                using Language relLang = new Language(relTsName);
                using Parser relParser = new Parser(relLang);
                using Tree? relTree = relParser.Parse(relatedContent);

                if (relTree != null)
                {
                    WalkContext relCtx = new WalkContext(relLangName);
                    AstWalker relWalker = new AstWalker(relCtx);
                    relWalker.Walk(relTree.RootNode);

                    result.StructuralDependencies = BuildStructuralDependencies(
                        relCtx, ctx, relatedPath);
                }
            }

            string json = JsonSerializer.Serialize(result, SerializerOptions);
            return (json, expandedDiff);
        }

        static void AnnotateModifications(List<FunctionInfo> functions, string diffContent)
        {
            HashSet<int> modifiedLines = DiffAnalyzer.ParseModifiedLines(diffContent);
            bool isNewFile = DiffAnalyzer.IsNewFile(diffContent);

            foreach (FunctionInfo fn in functions)
            {
                IEnumerable<int> fnLines = Enumerable.Range(fn.StartLine, fn.EndLine - fn.StartLine + 1);
                bool hasAnyChange = fnLines.Any(l => modifiedLines.Contains(l));
                if (!hasAnyChange) continue;

                bool allLinesAdded = fnLines.All(l => modifiedLines.Contains(l));
                fn.Change = (isNewFile || allLinesAdded) ? "added" : "modified";
            }
        }

        static StructuralDependencies? BuildStructuralDependencies(
            WalkContext relCtx, WalkContext mainCtx, string? relatedPath)
        {
            List<FunctionInfo> changedFns = mainCtx.Functions.Where(f => f.Change != null).ToList();
            HashSet<string> targetNames = (changedFns.Count > 0
                    ? changedFns.Select(f => f.QualifiedName)
                    : mainCtx.Functions.Select(f => f.QualifiedName))
                .Select(n => n.Split('.').Last())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            HashSet<string> mainTypeSimpleNames = mainCtx.Types
                .Select(t => t.QualifiedName.Split('.').Last())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<FunctionInfo> relevantFns = relCtx.Functions
                .Where(f => targetNames.Contains(f.QualifiedName.Split('.').Last()))
                .ToList();

            List<TypeInfo> relevantTypes = relCtx.Types
                .Where(t => mainTypeSimpleNames.Contains(t.QualifiedName.Split('.').Last()))
                .ToList();

            if (relevantFns.Count == 0 && relevantTypes.Count == 0) return null;

            return new StructuralDependencies
            {
                CounterpartContext = new CounterpartContext
                {
                    File = !string.IsNullOrEmpty(relatedPath) ? Path.GetFileName(relatedPath) : "",
                    Functions = relevantFns.Count > 0 ? relevantFns : null,
                    Types = relevantTypes.Count > 0 ? relevantTypes : null,
                }
            };
        }
    }
}
