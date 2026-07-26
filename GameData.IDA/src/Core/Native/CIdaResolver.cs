using System.Reflection;
using System.Runtime.InteropServices;

namespace GameData.IDA.Core.Native;

/// <summary>
/// The logical names of the two IDA libraries, used everywhere in place of a file name.
/// </summary>
/// <remarks>
/// These match the module keys the generator reads out of the committed export dumps
/// (<c>ida.exports.txt</c>, <c>idalib.exports.txt</c>), so the name a binding is attributed to and
/// the name it is loaded by are the same string on every platform.
/// </remarks>
internal static class IdaModules
{
    internal const string Kernel = "ida";
    internal const string Idalib = "idalib";
}

/// <summary>
/// Decides which file a logical IDA module name refers to, and loads it out of the configured
/// installation directory rather than the default probing paths.
/// </summary>
/// <remarks>
/// <para>
/// The directory comes from the <c>ida_path</c> convar, by way of the root
/// <see cref="CIdaNative"/> resolved when it registered this. No environment variable is involved.
/// </para>
/// <para>
/// <see cref="Resolve"/> is registered with <see cref="NativeLibrary.SetDllImportResolver"/> so
/// that a <c>[LibraryImport("ida")]</c> declaration anywhere in this assembly gets the same
/// answer. That callback is only consulted for P/Invoke call sites, though — it is <em>not</em>
/// reached by any <see cref="NativeLibrary.Load(string)"/> overload, including the one that takes
/// an assembly. The bindings load their libraries explicitly, so <see cref="Load"/> exists to give
/// them the identical policy; both routes share <see cref="TryResolve"/> and neither can drift
/// from the other.
/// </para>
/// </remarks>
internal static class CIdaResolver
{
    private static readonly Lock Gate = new();

    private static string? _root;
    private static bool _registered;

    /// <summary>
    /// Points the resolver at <paramref name="root"/>, registering it on first use.
    /// </summary>
    /// <remarks>
    /// <see cref="NativeLibrary.SetDllImportResolver"/> throws if it is called twice for the same
    /// assembly, so registration happens once and later calls only update the directory.
    /// </remarks>
    internal static void Register(string root)
    {
        lock (Gate)
        {
            _root = root;

            if (_registered)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(CIdaResolver).Assembly, Resolve);
            _registered = true;
        }
    }

    /// <summary>
    /// Loads an IDA module by logical name, applying the same policy as the P/Invoke resolver.
    /// </summary>
    /// <exception cref="DllNotFoundException">The library could not be loaded.</exception>
    internal static nint Load(string module)
    {
        nint handle = TryResolve(module);

        if (handle != nint.Zero)
        {
            return handle;
        }

        string fileName = IdaPlatform.LibraryFileName(module);
        throw new DllNotFoundException(
            $"Could not load '{fileName}'" +
            (string.IsNullOrEmpty(_root) ? "." : $" from '{_root}'.") +
            " Start with -ida_path <dir> pointing at an IDA installation.");
    }

    /// <summary>The <see cref="DllImportResolver"/> registered against this assembly.</summary>
    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        // Zero for anything that is not ours, which hands the request back to default probing.
        => libraryName is IdaModules.Kernel or IdaModules.Idalib ? TryResolve(libraryName) : nint.Zero;

    /// <summary>Returns a handle to the module, or zero if it could not be loaded.</summary>
    private static nint TryResolve(string module)
    {
        string fileName = IdaPlatform.LibraryFileName(module);
        string? root = _root;

        if (!string.IsNullOrEmpty(root)
            && NativeLibrary.TryLoad(Path.Combine(root, fileName), out nint handle))
        {
            return handle;
        }

        // No configured directory, or the file is not there: fall back to the platform's own
        // search path, which covers an IDA installation already on PATH / LD_LIBRARY_PATH.
        return NativeLibrary.TryLoad(fileName, out handle) ? handle : nint.Zero;
    }
}
