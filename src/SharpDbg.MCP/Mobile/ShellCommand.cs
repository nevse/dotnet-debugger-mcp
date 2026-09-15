using System.Diagnostics;
using System.Text;

namespace SharpDbg.MCP.Mobile;

/// <summary>
/// Runs a command line tool and waits for it. Everything mobile leans on tools this server does not
/// own - dotnet, adb, the Android emulator, xcrun - so they are all started the same way, with the
/// same rule about output: both pipes are drained on their own threads, because a build writing more
/// than a pipe buffer would otherwise block forever with nobody reading it.
///
/// A timeout kills the tool rather than abandoning it. These are all short-lived helpers, and one
/// left running would keep a pipe - and a device lock, in adb's case - held for the life of the
/// server.
/// </summary>
internal static class ShellCommand
{
    /// <summary>What the tool exited with, and everything it wrote</summary>
    internal sealed record Result(int ExitCode, IReadOnlyList<string> Output, IReadOnlyList<string> Errors)
    {
        public bool Success => ExitCode == 0;

        /// <summary>The whole of standard output as one string, which is how JSON arrives</summary>
        public string OutputText => string.Join(Environment.NewLine, Output);

        /// <summary>
        /// What to put in an error message. Standard error first, since that is where a tool
        /// explains itself, falling back to standard output for the ones that do not use it.
        /// </summary>
        public string Explain()
        {
            var said = Errors.Count > 0 ? Errors : Output;
            return said.Count == 0 ? $"exit code {ExitCode}" : string.Join(Environment.NewLine, said);
        }
    }

    /// <summary>
    /// Runs <paramref name="fileName"/> and returns once it has exited. <paramref name="onLine"/>
    /// sees every line of either stream as it arrives, for a caller that wants to log a long build
    /// rather than wait in silence for it; it runs on the reader threads, so it must not throw.
    /// </summary>
    public static Result Run(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string? workingDirectory = null,
        Action<string>? onLine = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (workingDirectory != null)
            startInfo.WorkingDirectory = workingDirectory;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}");

        var output = new List<string>();
        var errors = new List<string>();

        var readOutput = Task.Run(() => Drain(process.StandardOutput, output, onLine));
        var readErrors = Task.Run(() => Drain(process.StandardError, errors, onLine));

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            TryKill(process);
            throw new TimeoutException(
                $"{Path.GetFileName(fileName)} did not finish within {timeout.TotalSeconds:0.#}s and was stopped.");
        }

        // The overload without a timeout is what waits for the readers to see end-of-stream, so the
        // output is complete rather than whatever had arrived when the process exited
        process.WaitForExit();
        Task.WaitAll(readOutput, readErrors);

        return new Result(process.ExitCode, output, errors);
    }

    private static void Drain(StreamReader reader, List<string> into, Action<string>? onLine)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lock (into)
                into.Add(line);

            onLine?.Invoke(line);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone, or gone between the check and the call - either way there is nothing
            // left to stop, and the timeout is what the caller is told about
        }
    }

    /// <summary>
    /// The last <paramref name="lines"/> lines of a tool's output, for putting a failed build in a
    /// tool response without sending back a megabyte of MSBuild logging.
    /// </summary>
    public static string Tail(IReadOnlyList<string> output, int lines)
    {
        var builder = new StringBuilder();

        foreach (var line in output.Skip(Math.Max(0, output.Count - lines)))
            builder.AppendLine(line);

        return builder.ToString().TrimEnd();
    }
}
