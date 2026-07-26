using System.Text.RegularExpressions;

namespace GameData.IDA.Codegen;

/// <summary>
/// A C type as it appears in an SDK signature, reduced to the only three things that matter at
/// the ABI level: the base type name, how many levels of indirection, and whether it was written
/// as a reference (which is a pointer once compiled).
/// </summary>
internal sealed partial record CTypeRef(string BaseName, int PointerDepth, bool IsReference, bool IsFunctionPointer)
{
    /// <summary>How many stars the C# type needs. References are pointers.</summary>
    internal int Indirection => PointerDepth + (IsReference ? 1 : 0);

    [GeneratedRegex(@"\b(const|volatile|struct|class|enum|union|register|idaapi|NONNULL|_Nonnull|__stdcall|__cdecl|__fastcall)\b")]
    private static partial Regex Qualifiers { get; }

    [GeneratedRegex(@"^[A-Za-z_]\w*(\s*::\s*[A-Za-z_]\w*)*$")]
    private static partial Regex Identifier { get; }

    /// <summary>Parses a bare type with no declarator name, such as a return type.</summary>
    internal static bool TryParse(string text, out CTypeRef type)
    {
        type = null!;
        string s = Clean(text);

        if (s.Length == 0)
        {
            return false;
        }

        bool isReference = s.Contains('&');
        int depth = s.Count(c => c == '*');
        string baseName = s.Replace("*", " ").Replace("&", " ");
        baseName = HeaderScanner.Normalize(baseName);
        baseName = CollapseBuiltin(baseName);

        if (!Identifier.IsMatch(baseName))
        {
            return false;
        }

        type = new CTypeRef(baseName, depth, isReference, IsFunctionPointer: false);
        return true;
    }

    /// <summary>
    /// Parses a parameter declaration — a type plus, usually, a name. Falls back to a positional
    /// name (<c>a0</c>, <c>a1</c>, …) when the header declares the type only.
    /// </summary>
    internal static bool TryParseParameter(string text, int index, out CTypeRef type, out string name)
    {
        type = null!;
        name = $"a{index}";

        string s = Clean(text);

        // A function-pointer parameter such as `int (idaapi *cb)(void *ud)`. The ABI only needs a
        // code address, so it collapses to void* and the caller casts its own delegate* in.
        if (s.Contains('('))
        {
            var fp = FunctionPointerName().Match(s);
            if (fp.Success)
            {
                name = fp.Groups["name"].Value;
            }

            type = new CTypeRef("void", 1, IsReference: false, IsFunctionPointer: true);
            return true;
        }

        // `char *argv[]` and `ea_t eas[]` are pointers to the element type.
        int extraDepth = 0;
        while (s.EndsWith("[]", StringComparison.Ordinal))
        {
            s = s[..^2].TrimEnd();
            extraDepth++;
        }

        s = ArraySize().Replace(s, m =>
        {
            extraDepth++;
            return string.Empty;
        }).Trim();

        // Peel a trailing declarator name off, but only when something is left to be the type:
        // `int x` yields (int, x) while a lone `int` keeps its positional name.
        var tail = TrailingIdentifier().Match(s);
        if (tail.Success)
        {
            string remainder = s[..tail.Index].Trim();
            string candidate = tail.Groups["name"].Value;

            if (remainder.Length > 0 && !IsTypeWord(candidate))
            {
                name = candidate;
                s = remainder;
            }
        }

        if (!TryParse(s, out var parsed))
        {
            return false;
        }

        type = parsed with { PointerDepth = parsed.PointerDepth + extraDepth };
        return true;
    }

    private static string Clean(string text)
    {
        string s = Qualifiers.Replace(text, " ");
        s = s.Replace("*", " * ").Replace("&", " & ");
        return HeaderScanner.Normalize(s);
    }

    /// <summary>Folds multi-word builtins (<c>unsigned long long</c>) onto the SDK's own aliases.</summary>
    private static string CollapseBuiltin(string s) => s switch
    {
        "unsigned char" => "uchar",
        "signed char" => "int8",
        "unsigned short" => "ushort",
        "short int" => "short",
        "unsigned int" => "uint",
        "signed int" or "signed" => "int",
        "unsigned" => "uint",
        "unsigned long" => "ulong",
        "long int" => "long",
        "long long" or "long long int" => "longlong",
        "unsigned long long" or "unsigned long long int" => "ulonglong",
        "unsigned long int" => "ulong",
        "long double" => "double",
        _ => s,
    };

    /// <summary>
    /// Words that are part of a type rather than a declarator name, so that a nameless parameter
    /// such as <c>unsigned int</c> is not mistaken for a type <c>unsigned</c> named <c>int</c>.
    /// </summary>
    private static bool IsTypeWord(string word) => word is
        "char" or "short" or "int" or "long" or "float" or "double" or "void" or "bool" or
        "unsigned" or "signed";

    [GeneratedRegex(@"(?<name>[A-Za-z_]\w*)\s*$")]
    private static partial Regex TrailingIdentifier();

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex ArraySize();

    [GeneratedRegex(@"\*\s*(?<name>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex FunctionPointerName();
}
