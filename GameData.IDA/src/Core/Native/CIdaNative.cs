using GameData.IDA.Shared.Ida;

namespace GameData.IDA.Core.Native;

/// <summary>
/// Loads the IDA kernel and idalib libraries, works out which SDK version they are, resolves the
/// matching generated entry points against them, and brings the kernel up.
/// </summary>
/// <remarks>
/// This is the only hand-written interop in the module that has to be right about load order and
/// process state; everything below it is generated from the SDK headers. Which file a module name
/// refers to lives in <see cref="IdaPlatform"/>, where it is loaded from in
/// <see cref="CIdaResolver"/>, and which IDA it turned out to be in <see cref="IdaVersionProbe"/>.
/// No operating system API is called from any of them.
/// </remarks>
internal static unsafe class CIdaNative
{
    /// <summary>Directories every IDA installation has, used to tell a real one from a loose copy of the libraries.</summary>
    private static readonly string[] RequiredDirectories = ["cfg", "procs"];

    private static readonly Lock Gate = new();

    private static bool _attempted;
    private static string? _error;
    private static int _ownerThreadId;

    internal static bool IsInitialized { get; private set; }

    /// <summary>The directory the libraries were loaded from, once initialisation has succeeded.</summary>
    internal static string? Root { get; private set; }

    /// <summary>The SDK version whose bindings are live, once initialisation has succeeded.</summary>
    internal static IdaSdkVersion SdkVersion { get; private set; }

    /// <summary>
    /// Brings the kernel up, or returns the reason it could not be brought up. The first outcome
    /// is cached in both directions: a failed load leaves the process in a state where retrying
    /// tells you nothing new, so every later caller gets the original diagnosis.
    /// </summary>
    internal static bool TryInitialize(string? preferredRoot, IdaSdkVersion requested, out string? error)
    {
        lock (Gate)
        {
            if (!_attempted)
            {
                _attempted = true;
                _error = Initialize(preferredRoot, requested);
                IsInitialized = _error == null;
            }

            error = _error;
            return IsInitialized;
        }
    }

    private static string? Initialize(string? preferredRoot, IdaSdkVersion requested)
    {
        string root = ResolveRoot(preferredRoot);

        string kernelFile = IdaPlatform.LibraryFileName(IdaModules.Kernel);
        string idalibFile = IdaPlatform.LibraryFileName(IdaModules.Idalib);

        if (!File.Exists(Path.Combine(root, kernelFile)) || !File.Exists(Path.Combine(root, idalibFile)))
        {
            return $"'{kernelFile}' and '{idalibFile}' were not both found in '{root}'. " +
                   $"Start with -ida_path <dir> pointing at an IDA {SupportedVersions()} installation.";
        }

        // init_library() blocks indefinitely rather than failing when the kernel cannot find its
        // configuration, so an incomplete directory has to be rejected before it is called.
        foreach (string required in RequiredDirectories)
        {
            if (!Directory.Exists(Path.Combine(root, required)))
            {
                return $"'{root}' has the IDA libraries but no '{required}' directory, so it is not a " +
                       "complete IDA installation. idalib resolves its configuration, processor modules " +
                       "and licence relative to the directory the libraries were loaded from; start with " +
                       "-ida_path <dir> pointing at the installation root instead.";
            }
        }

        // Must happen before init_library: the kernel loads processor modules during database
        // setup and cannot recover from one that fails to bind.
        IdaPlatform.PreloadSupportLibraries(root);

        // Everything below loads by logical module name; CIdaResolver turns those into files in
        // this directory. Nothing here handles a path or a library file extension.
        CIdaResolver.Register(root);

        IdaSdkVersion selected;

        try
        {
            // Order matters: the idalib library imports from the kernel library.
            nint idaHandle = CIdaResolver.Load(IdaModules.Kernel);
            nint idalibHandle = CIdaResolver.Load(IdaModules.Idalib);

            // init_library has to come first, and by hand. idalib does not tolerate being called
            // before it — not even get_library_version, which takes the uninitialised path and
            // exits the process rather than returning. So the kernel is brought up through a
            // directly resolved pointer, and only then is there anything to ask about a version.
            if (!IdaBootstrap.TryInitLibrary(idalibHandle, out int status))
            {
                return $"'{idalibFile}' in '{root}' does not export init_library(), so it is not an " +
                       "idalib this project can drive. IDA 9.0 is the oldest release that ships one.";
            }

            if (status != 0)
            {
                return $"init_library() failed with status {status}. " +
                       $"'{root}' must be a complete IDA installation with a valid licence, " +
                       "not just the two libraries.";
            }

            if (!TrySelectVersion(idalibHandle, root, requested, out selected, out string? reason))
            {
                return reason;
            }

            // Everything from here on is version-specific: the entry points that get resolved and
            // the inftag_t ordinals that get read, both keyed off the one decision made above.
            IdaAbi.Select(selected);
            Ida.BindAll(idaHandle, idalibHandle, selected);
        }
        catch (Exception ex)
        {
            return $"Unable to load the IDA libraries from '{root}': {ex.Message}";
        }

        // IDA reports load and analysis problems through its own console and is silent by default.
        // Those messages are the only explanation available when the kernel decides to terminate
        // the process from inside open_database, so they are on from the start.
        Ida.enable_console_messages(0);

        _ownerThreadId = Environment.CurrentManagedThreadId;
        Root = root;
        SdkVersion = selected;
        return null;
    }

    /// <summary>
    /// Decides which generated binding set to resolve against: the <c>ida_sdk</c> convar when it
    /// names one, otherwise whatever the loaded idalib reports itself to be.
    /// </summary>
    /// <remarks>
    /// Nothing has been bound at this point, so a refusal here costs nothing and is recoverable by
    /// restarting with a different <c>-ida_path</c>. Binding first and discovering the mismatch
    /// afterwards is not: the wrong binder resolves every symbol that still exists under the same
    /// name and leaves the ones that changed shape pointing at the wrong signature.
    /// </remarks>
    /// <param name="idalib">An already-initialised idalib; see <see cref="IdaBootstrap"/>.</param>
    private static bool TrySelectVersion(
        nint idalib,
        string root,
        IdaSdkVersion requested,
        out IdaSdkVersion selected,
        out string? error)
    {
        error = null;

        if (requested != IdaSdkVersion.Auto)
        {
            selected = requested;

            if (!Ida.GeneratedVersions.Contains(requested))
            {
                error = $"-ida_sdk {requested} was requested, but this build only has bindings for " +
                        $"IDA {SupportedVersions()}.";
                return false;
            }

            return true;
        }

        selected = IdaSdkVersion.Auto;

        if (!IdaBootstrap.TryProbeVersion(idalib, out var version))
        {
            error = $"'{IdaPlatform.LibraryFileName(IdaModules.Idalib)}' in '{root}' " +
                    "does not report a version through get_library_version(), so it is not an idalib " +
                    "this project can drive. IDA 9.0 is the oldest release that ships one.";
            return false;
        }

        selected = IdaBootstrap.ToSdkVersion(version);

        if (selected == IdaSdkVersion.Auto || !Ida.GeneratedVersions.Contains(selected))
        {
            error = $"IDA {version} is installed, but this build only has bindings for " +
                    $"IDA {SupportedVersions()}. Vendor that SDK under thirdparty/ida-sdk and re-run " +
                    "GameData.IDA.Codegen, or start with -ida_sdk <version> to bind it as one of the " +
                    "versions listed above.";
            return false;
        }

        return true;
    }

    /// <summary>The SDK versions this build has generated binders for, as '9.0, 9.1'.</summary>
    private static string SupportedVersions()
        => string.Join(", ", Ida.GeneratedVersions.Select(Describe));

    /// <summary>Turns <c>V92</c> back into <c>9.2</c>.</summary>
    internal static string Describe(IdaSdkVersion version)
    {
        string name = version.ToString();
        return name.Length == 3 && name[0] == 'V' ? $"{name[1]}.{name[2]}" : name;
    }

    /// <summary>
    /// Resolves the installation directory: the <c>ida_path</c> convar if set, otherwise the
    /// nearest <c>binary</c> directory at or above the application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No environment variable is involved anywhere. IDA derives its own installation directory
    /// from the path the kernel library was loaded from, so loading it by absolute path is enough
    /// for the kernel to find its <c>cfg</c>, <c>procs</c>, <c>loaders</c> and licence.
    /// </para>
    /// <para>
    /// The walk upwards is what makes the repository layout work: the application builds into
    /// <c>build/Release/GameData.App</c> while <c>binary</c> sits at the repository root, and
    /// those libraries are far too large to copy into every output directory.
    /// </para>
    /// </remarks>
    private static string ResolveRoot(string? preferredRoot)
    {
        if (!string.IsNullOrWhiteSpace(preferredRoot))
        {
            return Path.GetFullPath(preferredRoot);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "binary");
            if (File.Exists(Path.Combine(candidate, IdaPlatform.LibraryFileName(IdaModules.Kernel))))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "binary");
    }

    /// <summary>
    /// Throws unless the caller is on the thread that initialised the kernel.
    /// </summary>
    /// <remarks>
    /// idalib is single-threaded and does not check this itself — calling in from another thread
    /// corrupts the database silently rather than failing. Every managed entry point asserts here
    /// first so that mistake surfaces as an exception instead of as bad analysis output.
    /// </remarks>
    internal static void AssertOwnerThread()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("The IDA kernel has not been initialized.");
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                $"idalib is single-threaded: it was initialized on managed thread {_ownerThreadId} " +
                $"but called from thread {Environment.CurrentManagedThreadId}.");
        }
    }

}
