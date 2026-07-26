using System.Diagnostics;
using System.Text;
using GameData.IDA.Shared.Ida;
using GameData.IDA.Shared.Interfaces;
using GameData.Tier0.Shared.Interfaces;
using GameData.Tier0.Shared.Logging;

namespace GameData.IDA.Core.Pool;

internal static class CIdaWorker
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(50);

    internal static int Run()
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var input = new StreamReader(Console.OpenStandardInput(), encoding);
        var output = new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = false };

        SilenceHumanOutput();

        var ida = InterfaceSystem.GetInterface<IIdaLibrary>(IdaInterfaceNames.IdaLibrary);
        if (ida == null)
        {
            Console.Error.WriteLine("No IIdaLibrary registered; this build cannot act as a worker.");
            return 1;
        }

        if (!ida.TryInitialize(out string? error))
        {
            Console.Error.WriteLine(error ?? "The IDA kernel could not be initialized.");
            return 1;
        }

        Send(output, new WorkerMessage(
            IdaProtocolKind.Ready,
            Sdk: ida.SdkVersion.ToString(),
            Version: ida.GetVersion().ToString()));

        while (input.ReadLine() is { } line)
        {
            var message = IdaProtocol.ReadPool(line);
            if (message == null)
            {
                continue;
            }

            if (message.T == IdaProtocolKind.Quit)
            {
                break;
            }

            if (message.T == IdaProtocolKind.Job)
            {
                RunJob(ida, output, message);
            }
        }

        ida.Close(save: false);
        return 0;
    }

    private static void RunJob(IIdaLibrary ida, TextWriter output, PoolMessage job)
    {
        if (string.IsNullOrWhiteSpace(job.Path))
        {
            Send(output, new WorkerMessage(IdaProtocolKind.Failed, job.Id, Message: "No path given."));
            return;
        }

        if (!File.Exists(job.Path))
        {
            Send(output, new WorkerMessage(IdaProtocolKind.Failed, job.Id, Message: $"No such file: '{job.Path}'."));
            return;
        }

        var clock = Stopwatch.StartNew();

        try
        {
            var nextReport = TimeSpan.Zero;

            int status = ida.Open(job.Path, runAutoAnalysis: true, onProgress: progress =>
            {
                if (clock.Elapsed < nextReport)
                {
                    return;
                }

                nextReport = clock.Elapsed + ReportInterval;

                Send(output, new WorkerMessage(
                    IdaProtocolKind.Progress,
                    job.Id,
                    Fraction: progress.Fraction,
                    Address: progress.Address.ToString("X")));
            });

            if (status != 0)
            {
                Send(output, new WorkerMessage(
                    IdaProtocolKind.Failed, job.Id,
                    Message: $"open_database() failed with status {status}."));
                return;
            }

            var done = new WorkerMessage(
                IdaProtocolKind.Done,
                job.Id,
                Functions: ida.FunctionCount,
                Segments: ida.SegmentCount,
                Strings: ida.StringCount,
                Milliseconds: clock.ElapsedMilliseconds);

            ida.Close(job.Save);
            Send(output, done);
        }
        catch (Exception ex)
        {
            TryClose(ida);
            Send(output, new WorkerMessage(IdaProtocolKind.Failed, job.Id, Message: ex.Message));
        }
    }

    private static void TryClose(IIdaLibrary ida)
    {
        try
        {
            ida.Close(save: false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to close the database after an error: {ex.Message}");
        }
    }

    private static void Send(TextWriter output, WorkerMessage message)
    {
        output.WriteLine(IdaProtocol.Write(message));
        output.Flush();
    }

    private static void SilenceHumanOutput()
    {
        var logging = InterfaceSystem.GetInterface<ILoggingSystem>(InterfaceNames.LoggingSystem);
        if (logging == null)
        {
            return;
        }

        for (int channel = 0; channel < logging.ChannelCount; channel++)
        {
            logging.SetChannelFlags(channel, logging.GetChannelFlags(channel) | LoggingChannelFlags.DoNotEcho);
        }

        logging.RegisterListener(new CWorkerLogListener());

        logging.SetResponsePolicy(new CWorkerResponsePolicy());
    }

    private sealed class CWorkerLogListener : ILoggingListener
    {
        public void Log(LoggingContext context, string message)
            => Console.Error.WriteLine($"[{context.ChannelName}] {message}");
    }

    private sealed class CWorkerResponsePolicy : ILoggingResponsePolicy
    {
        public LoggingResponse OnLog(LoggingContext context) => LoggingResponse.Continue;
    }
}
