using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using GameData.IDA.Core.Kernel;
using GameData.IDA.Shared.Ida;
using GameData.IDA.Shared.Interfaces;
using GameData.Tier0.Shared.Interfaces;

namespace GameData.IDA.Core.Pool;

[ExposeInterface(IdaInterfaceNames.IdaPool)]
internal sealed class CIdaPool : IIdaPool, IDisposable
{
    private static readonly TimeSpan QuitGrace = TimeSpan.FromSeconds(5);

    private const int MaxDiagnosticLines = 20;

    private readonly Lock _gate = new();
    private readonly List<Worker> _workers = [];

    private bool _disposed;

    public int Size => Math.Max(1, CIdaConVars.Cores);

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _workers.Count > 0;
            }
        }
    }

    public IReadOnlyList<IdaWorkerStatus> GetStatus()
    {
        lock (_gate)
        {
            return [.. _workers.Select(w => w.Status())];
        }
    }

    public IReadOnlyList<IdaBatchItem> RunBatch(
        IReadOnlyList<string> paths,
        bool save = true,
        Action<IdaBatchProgress>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            return [];
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        Start();

        var results = new IdaBatchItem?[paths.Count];
        var queue = new ConcurrentQueue<int>(Enumerable.Range(0, paths.Count));

        var pumps = new List<Thread>(_workers.Count);

        lock (_gate)
        {
            foreach (var worker in _workers)
            {
                var owner = worker;
                var pump = new Thread(() => Pump(owner, paths, save, queue, results, onProgress))
                {
                    IsBackground = true,
                    Name = $"ida-worker-{owner.Index}",
                };

                pumps.Add(pump);
            }
        }

        foreach (var pump in pumps)
        {
            pump.Start();
        }

        foreach (var pump in pumps)
        {
            pump.Join();
        }

        for (int i = 0; i < results.Length; i++)
        {
            results[i] ??= new IdaBatchItem(paths[i], false, 0, 0, 0, TimeSpan.Zero,
                "No worker was available to analyze this file.");
        }

        return [.. results.Select(r => r!)];
    }

    private void Pump(
        Worker worker,
        IReadOnlyList<string> paths,
        bool save,
        ConcurrentQueue<int> queue,
        IdaBatchItem?[] results,
        Action<IdaBatchProgress>? onProgress)
    {
        while (queue.TryDequeue(out int index))
        {
            string path = paths[index];

            if (!worker.IsAlive && !Respawn(worker))
            {
                results[index] = new IdaBatchItem(path, false, 0, 0, 0, TimeSpan.Zero,
                    $"Worker {worker.Index} could not be restarted.");
                continue;
            }

            var item = worker.Run(path, save, onProgress);
            results[index] = item;

            onProgress?.Invoke(new IdaBatchProgress(path, worker.Index, 1.0, null, true, item));
        }
    }

    private void Start()
    {
        lock (_gate)
        {
            for (int index = _workers.Count; index < Size; index++)
            {
                _workers.Add(Worker.Spawn(index));
            }
        }
    }

    private bool Respawn(Worker worker)
    {
        lock (_gate)
        {
            return worker.Restart();
        }
    }

    public void Stop()
    {
        List<Worker> workers;

        lock (_gate)
        {
            workers = [.. _workers];
            _workers.Clear();
        }

        foreach (var worker in workers)
        {
            worker.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private sealed class Worker(int index) : IDisposable
    {
        private static int _nextJobId;

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly Lock _gate = new();
        private readonly Queue<string> _diagnostics = new();

        private Process? _process;
        private string? _sdk;
        private string? _version;
        private string? _current;

        internal int Index => index;

        internal bool IsAlive
        {
            get
            {
                var process = _process;
                return process is { HasExited: false };
            }
        }

        internal static Worker Spawn(int index)
        {
            var worker = new Worker(index);
            worker.Restart();
            return worker;
        }

        internal IdaWorkerStatus Status()
        {
            lock (_gate)
            {
                return new IdaWorkerStatus(index, IsAlive, _sdk, _version, _current);
            }
        }

        internal bool Restart()
        {
            Kill();

            string? host = Environment.ProcessPath;
            if (string.IsNullOrEmpty(host))
            {
                Remember("Cannot start a worker: the host executable path is unknown.");
                return false;
            }

            var info = new ProcessStartInfo(host)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,

                StandardInputEncoding = Utf8NoBom,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };

            info.ArgumentList.Add("-ida_worker");
            info.ArgumentList.Add("1");

            if (!string.IsNullOrWhiteSpace(CIdaConVars.IdaPath))
            {
                info.ArgumentList.Add("-ida_path");
                info.ArgumentList.Add(CIdaConVars.IdaPath);
            }

            if (CIdaConVars.IdaSdk != IdaSdkVersion.Auto)
            {
                info.ArgumentList.Add("-ida_sdk");
                info.ArgumentList.Add(CIdaConVars.IdaSdk.ToString());
            }

            try
            {
                _process = Process.Start(info);
            }
            catch (Exception ex)
            {
                Remember($"Could not start worker {index}: {ex.Message}");
                return false;
            }

            if (_process == null)
            {
                return false;
            }

            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    Remember(e.Data);
                }
            };

            _process.BeginErrorReadLine();

            var ready = ReadUntil(IdaProtocolKind.Ready, IdaProtocolKind.Failed);
            if (ready?.T != IdaProtocolKind.Ready)
            {
                Remember($"Worker {index} did not become ready.");
                Kill();
                return false;
            }

            lock (_gate)
            {
                _sdk = ready.Sdk;
                _version = ready.Version;
            }

            return true;
        }

        internal IdaBatchItem Run(string path, bool save, Action<IdaBatchProgress>? onProgress)
        {
            string full = Path.GetFullPath(path);

            lock (_gate)
            {
                _current = full;
            }

            try
            {
                var process = _process;
                if (process == null || process.HasExited)
                {
                    return Failed(full, $"Worker {index} is not running.");
                }

                int jobId = Interlocked.Increment(ref _nextJobId);

                process.StandardInput.WriteLine(
                    IdaProtocol.Write(new PoolMessage(IdaProtocolKind.Job, jobId, full, save)));
                process.StandardInput.Flush();

                while (process.StandardOutput.ReadLine() is { } line)
                {
                    var message = IdaProtocol.ReadWorker(line);

                    switch (message?.T)
                    {
                        case IdaProtocolKind.Progress:
                            onProgress?.Invoke(new IdaBatchProgress(
                                full, index, message.Fraction, message.Address, false, null));
                            break;

                        case IdaProtocolKind.Done:
                            return new IdaBatchItem(full, true, message.Functions, message.Segments,
                                message.Strings, TimeSpan.FromMilliseconds(message.Milliseconds), null);

                        case IdaProtocolKind.Failed:
                            return Failed(full, message.Message ?? "The worker did not say why.");
                    }
                }

                string why = LastDiagnostics();
                return Failed(full, why.Length == 0
                    ? $"Worker {index} exited during analysis."
                    : $"Worker {index} exited during analysis: {why}");
            }
            catch (IOException ex)
            {
                return Failed(full, $"Lost contact with worker {index}: {ex.Message}");
            }
            finally
            {
                lock (_gate)
                {
                    _current = null;
                }
            }
        }

        private IdaBatchItem Failed(string path, string error)
            => new(path, false, 0, 0, 0, TimeSpan.Zero, error);

        private WorkerMessage? ReadUntil(params string[] kinds)
        {
            var process = _process;
            if (process == null)
            {
                return null;
            }

            try
            {
                while (process.StandardOutput.ReadLine() is { } line)
                {
                    var message = IdaProtocol.ReadWorker(line);
                    if (message != null && kinds.Contains(message.T, StringComparer.Ordinal))
                    {
                        return message;
                    }
                }
            }
            catch (IOException)
            {
            }

            return null;
        }

        private void Remember(string line)
        {
            lock (_gate)
            {
                _diagnostics.Enqueue(line);

                while (_diagnostics.Count > MaxDiagnosticLines)
                {
                    _diagnostics.Dequeue();
                }
            }
        }

        private string LastDiagnostics()
        {
            lock (_gate)
            {
                return _diagnostics.Count == 0 ? string.Empty : string.Join(" | ", _diagnostics);
            }
        }

        private void Kill()
        {
            var process = _process;
            _process = null;

            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        public void Dispose()
        {
            var process = _process;

            if (process is { HasExited: false })
            {
                try
                {
                    process.StandardInput.WriteLine(IdaProtocol.Write(new PoolMessage(IdaProtocolKind.Quit)));
                    process.StandardInput.Flush();
                    process.WaitForExit((int)QuitGrace.TotalMilliseconds);
                }
                catch (Exception)
                {
                }
            }

            Kill();
        }
    }
}
