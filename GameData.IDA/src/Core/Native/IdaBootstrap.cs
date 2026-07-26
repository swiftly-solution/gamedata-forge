using System.Runtime.InteropServices;
using GameData.IDA.Shared.Ida;

namespace GameData.IDA.Core.Native;

/// <summary>
/// The two idalib calls that have to happen before there are any bindings to make them through.
/// </summary>
/// <remarks>
/// <para>
/// Which generated binding set to resolve is a question about the installed version, and the only
/// way to ask is to call into the very library being negotiated with. So these two are resolved by
/// hand: <c>init_library</c>, because idalib exits the process rather than returning an error if
/// anything else is called first, and <c>get_library_version</c>, because it is the answer.
/// </para>
/// <para>
/// Both are safe to hand-resolve precisely because they are the boundary: their signatures have
/// been stable across every release of idalib, and a release where they were not would be one this
/// project could not load at all, by any route.
/// </para>
/// </remarks>
internal static unsafe class IdaBootstrap
{
    private const string InitSymbol = "init_library";
    private const string VersionSymbol = "get_library_version";

    /// <summary>
    /// Brings the kernel up. This must be the first call into <paramref name="idalib"/>: anything
    /// before it takes the library's uninitialised path, which terminates the process outright
    /// instead of failing.
    /// </summary>
    /// <param name="status">idalib's own status code, zero on success.</param>
    /// <returns><see langword="false"/> when the library does not export <c>init_library</c>.</returns>
    internal static bool TryInitLibrary(nint idalib, out int status)
    {
        status = 0;

        if (!NativeLibrary.TryGetExport(idalib, InitSymbol, out nint address))
        {
            return false;
        }

        status = ((delegate* unmanaged[Cdecl]<int, byte**, int>)address)(0, null);
        return true;
    }

    /// <summary>Reads the kernel version out of an already-initialised <paramref name="idalib"/>.</summary>
    /// <returns><see langword="false"/> when the library does not report one.</returns>
    internal static bool TryProbeVersion(nint idalib, out IdaVersion version)
    {
        version = default;

        if (!NativeLibrary.TryGetExport(idalib, VersionSymbol, out nint address))
        {
            return false;
        }

        var get = (delegate* unmanaged[Cdecl]<int*, int*, int*, byte>)address;

        int major = 0;
        int minor = 0;
        int build = 0;

        if (get(&major, &minor, &build) == 0)
        {
            return false;
        }

        version = new IdaVersion(major, minor, build);
        return true;
    }

    /// <summary>
    /// Maps a kernel version onto the SDK line whose bindings describe it, or
    /// <see cref="IdaSdkVersion.Auto"/> when there is no such line.
    /// </summary>
    /// <remarks>
    /// The mapping is exact on <c>major.minor</c> and deliberately does not fall back to the
    /// nearest older line. A 9.3 kernel is not a 9.2 kernel with extra symbols; binding it as one
    /// would resolve everything that still exists and silently call anything that changed shape
    /// through the wrong signature. Refusing is the only safe default, and <c>ida_sdk</c> is there
    /// for the case where the caller knows better.
    /// </remarks>
    internal static IdaSdkVersion ToSdkVersion(IdaVersion version)
        => Enum.TryParse($"V{version.Major}{version.Minor}", out IdaSdkVersion sdk) && sdk != IdaSdkVersion.Auto
            ? sdk
            : IdaSdkVersion.Auto;
}
