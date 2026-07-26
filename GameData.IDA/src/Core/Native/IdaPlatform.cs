using System.Runtime.InteropServices;

namespace GameData.IDA.Core.Native;

/// <summary>
/// The handful of things about loading IDA that differ between operating systems.
/// </summary>
/// <remarks>
/// Everything else in the module — the generated bindings included — is platform-neutral. IDA
/// exports plain C symbols with no name decoration, and on x64 there is a single calling
/// convention per platform, which <c>delegate* unmanaged[Cdecl]</c> maps to on all of them.
/// Nothing here calls into an operating system API; it is all <see cref="NativeLibrary"/>.
/// </remarks>
internal static class IdaPlatform
{
    /// <summary>
    /// The file a logical module name maps to on this platform: for <c>ida</c>, that is
    /// <c>ida.dll</c> on Windows, <c>libida.so</c> on Linux and <c>libida.dylib</c> on macOS.
    /// </summary>
    internal static string LibraryFileName(string module)
    {
        if (OperatingSystem.IsWindows())
        {
            return module + ".dll";
        }

        return OperatingSystem.IsMacOS() ? $"lib{module}.dylib" : $"lib{module}.so";
    }

    /// <summary>
    /// Loads the support libraries that sit beside the kernel, so the processor modules, loaders
    /// and plugins IDA opens later can bind against them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IDA loads <c>procs/pc.dll</c> and friends by full path, and those import siblings of the
    /// kernel such as <c>clp64.dll</c>. Windows resolves a dependency by full path against the
    /// <em>application</em> directory rather than the importing module's own, so without help
    /// every processor module fails with "the specified module could not be found" and the kernel
    /// gives up mid-load.
    /// </para>
    /// <para>
    /// Loading those siblings up front by absolute path fixes that with no operating system call:
    /// the loader satisfies an import from the already-loaded module table, matched on base file
    /// name, before it ever searches the disk. The cost is that they are mapped eagerly rather
    /// than on demand.
    /// </para>
    /// <para>
    /// Only Windows needs this. IDA's shared objects carry <c>$ORIGIN</c> / <c>@loader_path</c>
    /// run-paths, so on Linux and macOS a module's siblings already resolve relative to it.
    /// </para>
    /// </remarks>
    internal static void PreloadSupportLibraries(string root)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*.dll"))
        {
            // Never the kernels. `ida.dll` and `idalib.dll` are loaded deliberately, by logical
            // name through CIdaResolver; `ida32.dll` and `idalib32.dll` are the 32-bit-address
            // build of the same kernel and have no business in this process at all.
            if (IsKernelLibrary(Path.GetFileNameWithoutExtension(file)))
            {
                continue;
            }

            // A support library that will not load is only a problem if something later actually
            // needs it, and then it fails with a message naming it. Guessing here would be worse.
            NativeLibrary.TryLoad(file, out _);
        }
    }

    /// <remarks>
    /// These are the IDA 9 kernel names, which is the whole supported range: one 64-bit-address
    /// kernel called <c>ida</c> with a <c>32</c>-suffixed sibling. Pre-9 releases named the two
    /// <c>ida</c> and <c>ida64</c> instead, so <c>ida64</c> would fall through here and be
    /// preloaded as if it were a support library — worth remembering if that range is ever added.
    /// </remarks>
    private static bool IsKernelLibrary(string stem)
        => stem.StartsWith("ida", StringComparison.OrdinalIgnoreCase)
        && (stem.Length == 3 || stem is "ida32" or "idalib" or "idalib32");
}
