# GameData.IDA.Codegen

Generates the C# bindings in `GameData.IDA/src/Core/Native/Generated/` from the vendored IDA SDK
headers. Run it by hand after an SDK or IDA upgrade; the output is committed.

```
dotnet run --project GameData.IDA.Codegen
```

Defaults, both overridable:

| Argument      | Default                                    |
|---------------|--------------------------------------------|
| `--sdk-root`  | `thirdparty/ida-sdk`                       |
| `--out`       | `GameData.IDA/src/Core/Native/Generated`   |

## Vendored SDK layout

The SDK root holds one directory per vendored SDK, named after its `major.minor` line. That name is
the version's identity everywhere: it becomes the `IdaSdkVersion` member the runtime selects
(`9.2` → `V92`) and the subdirectory the binder for it is generated into.

```
thirdparty/ida-sdk/
  9.2/
    VERSION            9.2.0-sdk.1
    include/           the SDK headers, verbatim
    exports/           ida.exports.txt, idalib.exports.txt
  9.3/
    ...
```

Adding a version is adding a directory in this shape and re-running the generator. The only code
that has to change is `IdaSdkVersion`, which needs the matching member, and `IdaAbi`, which needs
that version's `inftag_t` row read off its `ida.hpp`. Both already cover 9.0 through 9.3.

## How it works

Per vendored version:

1. `HeaderScanner` reads every `idaman <ret> ida_export <name>(<args>);` declaration, plus the
   typedef, `using`, `#define`, enum, array-alias, `OPAQUE_HANDLE` and function-type universe
   needed to resolve them.
2. `TypeTable` maps each C type to C#. Aliases guarded by `#ifdef __EA64__` are pinned to the
   64-bit branch, since every IDA 9 release ships a single 64-bit-address kernel.
3. `ExportIndex` reads that version's committed export listings. **Only symbols observed in a real
   export table are emitted**, and each is tagged with the library it came from — which is what
   lets the generated binder resolve everything eagerly and treat a miss as a hard error.
4. `DeclRenderer` turns the surviving declarations into their C# form.

Then across versions:

5. `SurfaceMerger` folds them into one binding surface. `Ida` holds a single static field per
   symbol, so a symbol appears once no matter how many versions are vendored.
6. `Emitter` writes the two halves that split apart:
   - the **surface** — `Ida.<Header>.g.cs`, one field and one forwarder per symbol, emitted once
     from the union — plus `Ida.Opaque.g.cs` and `Ida.Sdk.g.cs`, which carries the version dispatch;
   - the **binder** — `<Version>/Ida.<Header>.Bind.g.cs` and `<Version>/Ida.Bind.g.cs`, the
     `GetExport` calls, emitted once per version.
7. `_codegen-report.txt` summarises each version and what was skipped and why.

Selecting a version at load time is then just a matter of calling the right `BindAll_<V>`, which
`CIdaNative` does after asking the installed idalib what it is.

### Symbols that are not in every version

A symbol only some versions export still gets one field and one forwarder. The versions that lack
it simply never assign the field, and the forwarder checks for null and throws naming the symbol.
Symbols present everywhere — nearly all of them — are emitted unguarded, so the common path costs
nothing.

A symbol whose *signature* differs between versions is a different matter: one field cannot serve
both, and picking a shape would compile fine and then call through the wrong signature on the other
version. Those are left off the surface entirely and listed in the report, where each one is a
prompt to write a deliberate per-version shim.

## Refreshing the export dumps

Needed only when the IDA binaries change. Three dump shapes are accepted and detected on content,
so the dumps can be produced from whatever that version happens to ship — the SDK download has
import libraries, an installation has the runtime libraries. Write them into the directory of the
version they describe:

```
:: Windows, from an IDA installation
dumpbin /EXPORTS binary\ida.dll    > thirdparty\ida-sdk\9.2\exports\ida.exports.txt
dumpbin /EXPORTS binary\idalib.dll > thirdparty\ida-sdk\9.2\exports\idalib.exports.txt
```

```
:: Windows, from an SDK download — no installation needed
dumpbin /EXPORTS lib\x64_win_vc_64\ida.lib    > thirdparty\ida-sdk\9.3\exports\ida.exports.txt
dumpbin /EXPORTS lib\x64_win_vc_64\idalib.lib > thirdparty\ida-sdk\9.3\exports\idalib.exports.txt
```

```sh
# Linux / macOS
nm --dynamic --defined-only binary/libida.so    > thirdparty/ida-sdk/9.2/exports/ida.exports.txt
nm --dynamic --defined-only binary/libidalib.so > thirdparty/ida-sdk/9.2/exports/idalib.exports.txt
```

An import library records what a symbol is called but not where it will live, so its rows carry the
name alone with the ordinal and address columns empty. That is also what dumpbin's own section
labels look like, so the export table is read as a bounded section — from the `ordinal … name`
column header to the `Summary` — rather than by matching rows anywhere in the file.

The file **stem** is the module identity — `ida.exports.txt` describes the module `ida`, whatever
the library is called on a given platform. Generated code never mentions a library file name.

## What is deliberately not generated

- **Variadic and `va_list` functions** (`qsnprintf`, `qsscanf`, `qvfprintf`, …). Listed in the
  report; C# has no business calling them.
- **Three declarations with unrepresentable by-value parameters**: `btoa128` (`__int128`),
  `create_nodeval_merge_handler` (`std::function`) and `bookmarks_t_set_desc`, which takes a
  `qstring` by value — the single by-value C++ class parameter in the entire export surface.
- **Constants.** IDA's flags live in `#define`s and unscoped enums whose bodies are arithmetic
  over other macros. They are transcribed by hand into `IdaConstants` as they are needed rather
  than half-parsed. The XML doc on every generated function carries its original C declaration,
  so the flag type each parameter expects is visible at the call site. Values that are *ordinal
  positions* in an SDK enum rather than stable flag bits — the `inftag_t` tags — live in `IdaAbi`
  instead, keyed by version, because those do shift between releases.
