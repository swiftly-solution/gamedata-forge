namespace GameData.IDA.Codegen;

/// <summary>Maps SDK C types onto the C# types used in the generated <c>delegate*</c> signatures.</summary>
internal sealed class TypeTable
{
    /// <summary>
    /// Aliases whose definition is guarded by <c>#ifdef __EA64__</c>. The header scanner sees both
    /// branches, so these are pinned rather than inferred. Every IDA 9 release ships a single
    /// 64-bit-address kernel and every 9.x SDK defaults <c>__EA64__</c> on, so the 64-bit branch is
    /// the right one for the whole supported range. This is the assumption that would have to be
    /// revisited first if a pre-9 SDK were ever vendored: there the choice is per library rather
    /// than per SDK, and one binding surface could not cover both.
    /// </summary>
    private static readonly Dictionary<string, string> Ea64Aliases = new(StringComparer.Ordinal)
    {
        ["ea_t"] = "ulong",
        ["asize_t"] = "ulong",
        ["adiff_t"] = "long",
        ["uval_t"] = "ulong",
        ["sval_t"] = "long",
        ["sel_t"] = "ulong",
        ["nodeidx_t"] = "ulong",
        ["tid_t"] = "ulong",
        ["ea32_t"] = "uint",
        ["ea64_t"] = "ulong",
    };

    /// <summary>Root C types. Widths are MSVC x64: <c>long</c> is 32-bit, <c>bool</c> is one byte.</summary>
    private static readonly Dictionary<string, string> Primitives = new(StringComparer.Ordinal)
    {
        ["void"] = "void",
        // C++ bool is a single byte. Never System.Boolean: its width inside a function-pointer
        // signature is not something the runtime guarantees.
        ["bool"] = "byte",
        ["char"] = "sbyte",
        ["uchar"] = "byte",
        ["short"] = "short",
        ["ushort"] = "ushort",
        ["int"] = "int",
        ["uint"] = "uint",
        ["long"] = "int",
        ["ulong"] = "uint",
        ["longlong"] = "long",
        ["ulonglong"] = "ulong",
        ["int8"] = "sbyte",
        ["uint8"] = "byte",
        ["int16"] = "short",
        ["uint16"] = "ushort",
        ["int32"] = "int",
        ["uint32"] = "uint",
        ["int64"] = "long",
        ["uint64"] = "ulong",
        ["float"] = "float",
        ["double"] = "double",
        ["size_t"] = "nuint",
        ["ssize_t"] = "nint",
        ["ptrdiff_t"] = "nint",
        ["intptr_t"] = "nint",
        ["uintptr_t"] = "nuint",
        ["wchar16_t"] = "ushort",
        ["wchar32_t"] = "uint",
        ["time_t"] = "long",
        ["FILE"] = "void",
        ["va_list"] = "void",
    };

    /// <summary>
    /// Types declared by hand in <c>GameData.IDA/src/Core/Native</c>. The generator references
    /// them but must not emit an opaque duplicate.
    /// </summary>
    private static readonly HashSet<string> HandWritten = new(StringComparer.Ordinal)
    {
        "qvector", "qstring", "qwstring", "qstrvec_t", "range_t", "auto_display_t",
    };

    private readonly IReadOnlyDictionary<string, string> _typedefs;
    private readonly IReadOnlyDictionary<string, string> _enums;
    private readonly IReadOnlyDictionary<string, string> _arrays;
    private readonly IReadOnlySet<string> _pointers;
    private readonly IReadOnlySet<string> _functions;
    private readonly SortedSet<string> _opaque = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _emittedEnums = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _unresolved = new(StringComparer.Ordinal);

    internal TypeTable(ScanResult scan)
    {
        _typedefs = scan.Typedefs;
        _enums = scan.Enums;
        _arrays = scan.ArrayAliases;
        _pointers = scan.PointerAliases;
        _functions = scan.FunctionTypes;
    }

    /// <summary>Opaque struct names the emitter must declare so typed pointers stay distinct.</summary>
    internal IReadOnlyCollection<string> OpaqueTypes => _opaque;

    /// <summary>Enum aliases actually reached from a signature, mapped to their C# underlying type.</summary>
    internal IReadOnlyDictionary<string, string> UsedEnums => _emittedEnums;

    /// <summary>Base type names that could not be resolved to a value type.</summary>
    internal IReadOnlyCollection<string> Unresolved => _unresolved;

    /// <summary>
    /// Renders <paramref name="type"/> as C#. Returns <see langword="false"/> when the base type
    /// is an unknown value type — passing one of those by value would be a silent ABI mismatch,
    /// so the caller drops the whole declaration instead of guessing a width.
    /// </summary>
    internal bool TryRender(CTypeRef type, out string rendered)
    {
        rendered = string.Empty;
        int stars = type.Indirection;

        // `char *` and `uchar *` are strings and byte buffers, never sbyte* — the whole binding
        // surface treats them as UTF-8 bytes.
        if (stars > 0 && type.BaseName is "char" or "uchar" or "int8" or "uint8")
        {
            rendered = "byte" + new string('*', stars);
            return true;
        }

        // A function type is only ever reachable as an address, whether the header wrote it as a
        // pointer or let a bare function type decay to one. Callers cast their own delegate* in.
        if (_functions.Contains(type.BaseName))
        {
            rendered = "void" + new string('*', Math.Max(stars, 1));
            return true;
        }

        // OPAQUE_HANDLE aliases are already pointers, so the declared indirection is one deeper
        // than it looks.
        if (_pointers.Contains(type.BaseName))
        {
            string pointee = "__" + Sanitize(type.BaseName);
            _opaque.Add(pointee);
            rendered = pointee + new string('*', stars + 1);
            return true;
        }

        // An array alias decays to a pointer to its element type.
        if (_arrays.TryGetValue(type.BaseName, out string? element))
        {
            string? resolvedElement = ResolveBase(element, isPointer: false);
            if (resolvedElement != null)
            {
                rendered = resolvedElement + new string('*', stars + 1);
                return true;
            }
        }

        string? baseName = ResolveBase(type.BaseName, stars > 0);

        if (baseName == null)
        {
            if (stars > 0)
            {
                // A pointer to something we cannot describe is still just an address. Give it a
                // distinct opaque struct so `func_t*` never silently unifies with `segment_t*`.
                string opaque = Sanitize(type.BaseName);
                if (!HandWritten.Contains(opaque))
                {
                    _opaque.Add(opaque);
                }

                rendered = opaque + new string('*', stars);
                return true;
            }

            _unresolved.Add(type.BaseName);
            return false;
        }

        rendered = baseName + new string('*', stars);
        return true;
    }

    /// <summary>
    /// Walks typedef chains down to a primitive. Enums resolve to their underlying integer type
    /// via a generated alias so the emitted signature still reads like the header.
    /// </summary>
    private string? ResolveBase(string name, bool isPointer)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string current = name;

        while (true)
        {
            if (Ea64Aliases.TryGetValue(current, out string? pinned))
            {
                return pinned;
            }

            if (Primitives.TryGetValue(current, out string? primitive))
            {
                return primitive == "void" && !isPointer && current != "void" ? null : primitive;
            }

            if (HandWritten.Contains(current))
            {
                return isPointer ? Sanitize(current) : null;
            }

            if (_enums.TryGetValue(current, out string? underlying))
            {
                string? resolved = ResolveBase(underlying, isPointer: false);
                if (resolved == null)
                {
                    return null;
                }

                _emittedEnums[Sanitize(current)] = resolved;
                return resolved;
            }

            if (!_typedefs.TryGetValue(current, out string? next))
            {
                // Nested types such as `encoder_t::notify_recerr_t` are indexed under their
                // unqualified name, which is unique across the SDK headers.
                int sep = current.LastIndexOf("::", StringComparison.Ordinal);
                if (sep < 0)
                {
                    return null;
                }

                next = current[(sep + 2)..];
            }

            if (!seen.Add(current))
            {
                return null;
            }

            current = next;
        }
    }

    /// <summary>Strips namespace qualification and escapes anything C# would reject as an identifier.</summary>
    internal static string Sanitize(string name)
    {
        int sep = name.LastIndexOf("::", StringComparison.Ordinal);
        string s = sep >= 0 ? name[(sep + 2)..] : name;
        return CSharpKeywords.Contains(s) ? "@" + s : s;
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };
}
