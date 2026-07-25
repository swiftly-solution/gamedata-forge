using System.Reflection;
using System.Runtime.Loader;

namespace GameData.DepotDownloader.Core.Depot;

internal static class CDepotNatives
{
    private const string AssemblyName = "DepotDownloader";

    private static readonly Lock _lock = new();
    private static bool _resolved;
    private static string? _error;

    private static Type? _contentDownloader;
    private static FieldInfo? _configField;
    private static MethodInfo? _initializeSteam3;
    private static MethodInfo? _shutdownSteam3;
    private static MethodInfo? _downloadAppAsync;
    private static MethodInfo? _loadAccountSettings;
    private static FieldInfo? _depotConfigStoreInstance;

    internal static bool TryResolve(out string? error)
    {
        lock (_lock)
        {
            if (!_resolved)
            {
                _resolved = true;
                _error = Resolve();
            }

            error = _error;
            return error == null;
        }
    }

    private static string? Resolve()
    {
        try
        {
            AssemblyLoadContext.Default.Resolving += ProbeBaseDirectory;

            var assembly = Assembly.Load(AssemblyName);

            _contentDownloader = assembly.GetType("DepotDownloader.ContentDownloader", throwOnError: true)!;
            var accountSettingsStore = assembly.GetType("DepotDownloader.AccountSettingsStore", throwOnError: true)!;
            var depotConfigStore = assembly.GetType("DepotDownloader.DepotConfigStore", throwOnError: true)!;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            _configField = _contentDownloader.GetField("Config", flags);
            _initializeSteam3 = _contentDownloader.GetMethod("InitializeSteam3", flags);
            _shutdownSteam3 = _contentDownloader.GetMethod("ShutdownSteam3", flags);
            _downloadAppAsync = _contentDownloader.GetMethod("DownloadAppAsync", flags);
            _loadAccountSettings = accountSettingsStore.GetMethod("LoadFromFile", flags);
            _depotConfigStoreInstance = depotConfigStore.GetField("Instance", flags);

            foreach (var (member, name) in new (MemberInfo?, string)[]
            {
                (_configField, "ContentDownloader.Config"),
                (_initializeSteam3, "ContentDownloader.InitializeSteam3"),
                (_shutdownSteam3, "ContentDownloader.ShutdownSteam3"),
                (_downloadAppAsync, "ContentDownloader.DownloadAppAsync"),
                (_loadAccountSettings, "AccountSettingsStore.LoadFromFile"),
                (_depotConfigStoreInstance, "DepotConfigStore.Instance"),
            })
            {
                if (member == null)
                {
                    return $"{AssemblyName}.dll is present but '{name}' was not found; " +
                           "the bundled DepotDownloader build is not the expected one.";
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Unable to load {AssemblyName}.dll: {ex.Message}";
        }
    }

    private static Assembly? ProbeBaseDirectory(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name == null)
        {
            return null;
        }

        string candidate = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    }

    internal static object Config => _configField!.GetValue(null)!;

    internal static void LoadAccountSettings(string path)
        => _loadAccountSettings!.Invoke(null, [path]);

    internal static bool InitializeSteam3(string? username, string? password)
        => (bool)_initializeSteam3!.Invoke(null, [username, password])!;

    internal static void ShutdownSteam3() => _shutdownSteam3!.Invoke(null, null);

    internal static void ResetDepotConfigStore() => _depotConfigStoreInstance!.SetValue(null, null);

    internal static Task DownloadAppAsync(uint appId, List<(uint, ulong)> depotManifestIds,
        string branch, string? os, string? arch, string? language, bool lowViolence, bool isUgc)
        => (Task)_downloadAppAsync!.Invoke(null,
            [appId, depotManifestIds, branch, os, arch, language, lowViolence, isUgc])!;

    internal static void SetConfig(string name, object? value)
    {
        var config = Config;
        var property = config.GetType().GetProperty(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"DownloadConfig.{name} not found.");

        property.SetValue(config, value);
    }
}
