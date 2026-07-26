using System.Text;
using System.Text.RegularExpressions;

namespace GameData.IDA.Codegen;

/// <summary>A single <c>idaman ... ida_export</c> declaration lifted out of an SDK header.</summary>
internal sealed record NativeDecl(
    string Header,
    string Name,
    CTypeRef ReturnType,
    IReadOnlyList<NativeParam> Parameters,
    bool IsData,
    string RawSignature);

internal sealed record NativeParam(CTypeRef Type, string Name, string? DefaultValue);

/// <summary>
/// Pulls exported declarations and the typedef/enum universe out of the vendored SDK headers.
/// </summary>
/// <remarks>
/// The SDK declares every exported symbol in exactly one shape —
/// <c>idaman &lt;ret&gt; ida_export &lt;name&gt;(&lt;args&gt;);</c> — which is what makes a
/// regex scanner sufficient here instead of a real C++ parser.
/// </remarks>
internal static partial class HeaderScanner
{
    /// <remarks>
    /// The argument group is greedy so that it swallows the inner parentheses of a
    /// function-pointer parameter and backtracks to the closing parenthesis before the semicolon.
    /// A lazy group stops at the first inner <c>)</c> and loses those declarations entirely.
    /// </remarks>
    [GeneratedRegex(@"(?s)idaman\s+(?<ret>[^;{}]*?)\bida_export(?<data>_data)?\s+(?<name>[*&]*[A-Za-z_]\w*)\s*(?<args>\([^;{}]*\))?\s*;")]
    private static partial Regex ExportDecl { get; }

    [GeneratedRegex(@"typedef\s+(?<from>[A-Za-z_]\w*(?:\s+[A-Za-z_]\w*)*)\s+(?<to>[A-Za-z_]\w*)\s*;")]
    private static partial Regex SimpleTypedef { get; }

    /// <summary>C++11 alias form, as in <c>using aflags_t = flags_t;</c>.</summary>
    [GeneratedRegex(@"\busing\s+(?<to>[A-Za-z_]\w*)\s*=\s*(?<from>[A-Za-z_]\w*(?:\s+[A-Za-z_]\w*)*)\s*;")]
    private static partial Regex UsingAlias { get; }

    /// <summary>An alias defined by the preprocessor, as in <c>#define qoff64_t int64</c>.</summary>
    [GeneratedRegex(@"^[ \t]*#[ \t]*define[ \t]+(?<to>[A-Za-z_]\w*)[ \t]+(?<from>[A-Za-z_]\w*)[ \t]*(?://.*)?$", RegexOptions.Multiline)]
    private static partial Regex DefineAlias { get; }

    /// <summary><c>typedef uint16 eNI[IEEE_NI];</c> — an array alias, which is a pointer in a parameter.</summary>
    [GeneratedRegex(@"typedef\s+(?<from>[A-Za-z_]\w*(?:\s+[A-Za-z_]\w*)*)\s+(?<to>[A-Za-z_]\w*)\s*\[[^\]]*\]\s*;")]
    private static partial Regex ArrayTypedef { get; }

    /// <summary>
    /// <c>OPAQUE_HANDLE(qthread_t)</c> expands to <c>typedef struct __qthread_t {} *qthread_t</c>,
    /// so the alias is a pointer to a type with no accessible layout.
    /// </summary>
    [GeneratedRegex(@"\bOPAQUE_HANDLE\(\s*(?<name>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex OpaqueHandle { get; }

    [GeneratedRegex(@"\benum\s+(?<name>[A-Za-z_]\w*)\s*(?::\s*(?<base>[A-Za-z_][\w ]*?)\s*)?\{")]
    private static partial Regex EnumDecl { get; }

    /// <summary>Any typedef that declares a function or function-pointer type.</summary>
    [GeneratedRegex(@"(?s)\btypedef\b(?<body>[^;]*\([^;]*\))\s*;")]
    private static partial Regex FunctionTypedef { get; }

    /// <summary>Decorators that sit between <c>idaman</c> and the real return type.</summary>
    [GeneratedRegex(@"\b(THREAD_SAFE|NORETURN|DEPRECATED|IEEE_DEPRECATED|newapi|inline|extern|EXTERNC)\b|AS_PRINTF\s*\(\s*\d+\s*,\s*\d+\s*\)|AS_SCANF\s*\(\s*\d+\s*,\s*\d+\s*\)|AS_STRFTIME\s*\(\s*\d+\s*\)")]
    private static partial Regex ReturnDecorators { get; }

    internal static ScanResult Scan(string includeDir)
    {
        var decls = new List<NativeDecl>();
        var typedefs = new Dictionary<string, string>(StringComparer.Ordinal);
        var enums = new Dictionary<string, string>(StringComparer.Ordinal);
        var arrays = new Dictionary<string, string>(StringComparer.Ordinal);
        var pointers = new HashSet<string>(StringComparer.Ordinal);
        var functions = new HashSet<string>(StringComparer.Ordinal);
        var malformed = new List<string>();

        foreach (var file in Directory.EnumerateFiles(includeDir)
                     .Where(f => f.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)
                              || f.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string header = Path.GetFileName(file);
            string raw = File.ReadAllText(file);
            string text = StripNoise(raw);

            // Read #define aliases off the raw text: StripNoise drops directive lines, and a few
            // SDK types (qoff64_t) are defined that way rather than with a typedef.
            foreach (Match m in DefineAlias.Matches(raw))
            {
                typedefs.TryAdd(m.Groups["to"].Value, m.Groups["from"].Value);
            }

            foreach (Match m in ArrayTypedef.Matches(text))
            {
                arrays.TryAdd(m.Groups["to"].Value, Normalize(m.Groups["from"].Value));
            }

            foreach (Match m in OpaqueHandle.Matches(text))
            {
                pointers.Add(m.Groups["name"].Value);
            }

            foreach (Match m in FunctionTypedef.Matches(text))
            {
                string? name = FunctionTypedefName(m.Groups["body"].Value);
                if (name != null)
                {
                    functions.Add(name);
                }
            }

            foreach (Match m in SimpleTypedef.Matches(text))
            {
                // Later definitions of the same alias are #ifdef variants (32- vs 64-bit builds);
                // TypeTable pins those explicitly, so first-wins is fine for everything else.
                typedefs.TryAdd(m.Groups["to"].Value, Normalize(m.Groups["from"].Value));
            }

            foreach (Match m in UsingAlias.Matches(text))
            {
                typedefs.TryAdd(m.Groups["to"].Value, Normalize(m.Groups["from"].Value));
            }

            foreach (Match m in EnumDecl.Matches(text))
            {
                // An enum without an explicit underlying type is int under MSVC, which is the
                // only ABI ida.dll is ever built with on Windows.
                string underlying = m.Groups["base"].Success ? Normalize(m.Groups["base"].Value) : "int";
                enums.TryAdd(m.Groups["name"].Value, underlying);
            }

            foreach (Match m in ExportDecl.Matches(text))
            {
                if (TryParseDecl(header, m, out var decl, out string? reason))
                {
                    decls.Add(decl);
                }
                else
                {
                    malformed.Add($"{header}: {reason} :: {Normalize(m.Value)}");
                }
            }
        }

        return new ScanResult(decls, typedefs, enums, arrays, pointers, functions, malformed);
    }

    /// <summary>
    /// Extracts the alias name from a typedef that declares a function or function-pointer type.
    /// Both <c>typedef int idaapi cb_t(void *ud)</c> and <c>typedef int (idaapi *cb_t)(void *ud)</c>
    /// name the alias immediately before the final parameter list, optionally behind a <c>)</c>.
    /// </summary>
    private static string? FunctionTypedefName(string body)
    {
        int open = body.LastIndexOf('(');
        if (open < 0)
        {
            return null;
        }

        int i = open - 1;
        while (i >= 0 && (char.IsWhiteSpace(body[i]) || body[i] == ')'))
        {
            i--;
        }

        int end = i + 1;
        while (i >= 0 && (char.IsLetterOrDigit(body[i]) || body[i] == '_'))
        {
            i--;
        }

        return end - i - 1 > 0 ? body[(i + 1)..end] : null;
    }

    private static bool TryParseDecl(string header, Match m, out NativeDecl decl, out string? reason)
    {
        decl = null!;
        reason = null;

        string rawName = m.Groups["name"].Value;
        string name = rawName.TrimStart('*', '&');

        // Stars written against the name belong to the return type: `func_t *ida_export get_func`.
        string returnStars = new('*', rawName.Length - name.Length);
        string returnText = Normalize(ReturnDecorators.Replace(m.Groups["ret"].Value, " ")) + returnStars;

        if (!CTypeRef.TryParse(returnText, out var returnType))
        {
            reason = "unparseable return type";
            return false;
        }

        bool isData = m.Groups["data"].Success;
        if (isData)
        {
            decl = new NativeDecl(header, name, returnType, [], true, Normalize(m.Value));
            return true;
        }

        if (!m.Groups["args"].Success)
        {
            reason = "function declaration without an argument list";
            return false;
        }

        string args = m.Groups["args"].Value;
        args = args[1..^1]; // drop the enclosing parentheses

        var parameters = new List<NativeParam>();
        int index = 0;

        foreach (string chunk in SplitTopLevel(args))
        {
            string arg = Normalize(chunk);
            if (arg.Length == 0 || arg == "void")
            {
                continue;
            }

            if (arg == "...")
            {
                reason = "variadic";
                return false;
            }

            string? defaultValue = null;
            int eq = IndexOfTopLevel(arg, '=');
            if (eq >= 0)
            {
                defaultValue = arg[(eq + 1)..].Trim();
                arg = arg[..eq].Trim();
            }

            if (!CTypeRef.TryParseParameter(arg, index, out var type, out string paramName))
            {
                reason = $"unparseable parameter '{arg}'";
                return false;
            }

            if (type.BaseName == "va_list")
            {
                reason = "va_list";
                return false;
            }

            parameters.Add(new NativeParam(type, paramName, defaultValue));
            index++;
        }

        decl = new NativeDecl(header, name, returnType, parameters, false, Normalize(m.Value));
        return true;
    }

    /// <summary>
    /// Removes comments and preprocessor directives. Directive bodies are kept: dropping only the
    /// <c>#if</c>/<c>#else</c>/<c>#endif</c> lines leaves both branches of the 32/64-bit typedef
    /// pairs visible, and <see cref="TypeTable"/> pins those aliases explicitly anyway.
    /// </summary>
    private static string StripNoise(string text)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                sb.Append(' ');
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
                sb.Append(' ');
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }

        var lines = sb.ToString().Split('\n');
        var kept = new StringBuilder(text.Length);

        foreach (string line in lines)
        {
            if (!line.TrimStart().StartsWith('#'))
            {
                kept.Append(line);
            }

            kept.Append('\n');
        }

        return kept.ToString();
    }

    /// <summary>Splits an argument list on commas that are not nested inside parentheses or brackets.</summary>
    private static IEnumerable<string> SplitTopLevel(string args)
    {
        int depth = 0;
        int start = 0;

        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (c is '(' or '[' or '<')
            {
                depth++;
            }
            else if (c is ')' or ']' or '>')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                yield return args[start..i];
                start = i + 1;
            }
        }

        yield return args[start..];
    }

    private static int IndexOfTopLevel(string s, char target)
    {
        int depth = 0;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth--;
            }
            else if (c == target && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    internal static string Normalize(string s) => Whitespace().Replace(s, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

internal sealed record ScanResult(
    IReadOnlyList<NativeDecl> Declarations,
    IReadOnlyDictionary<string, string> Typedefs,
    IReadOnlyDictionary<string, string> Enums,
    IReadOnlyDictionary<string, string> ArrayAliases,
    IReadOnlySet<string> PointerAliases,
    IReadOnlySet<string> FunctionTypes,
    IReadOnlyList<string> Malformed);
