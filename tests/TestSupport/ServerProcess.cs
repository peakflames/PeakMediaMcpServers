namespace PeakMediaTestSupport;

/// <summary>
/// Runs a built server as a real child process (<c>dotnet &lt;dll&gt; args…</c>), capturing standard
/// output and standard error separately. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>,
/// never a shell string. Settings that could leak in from the developer's shell are removed first.
/// </summary>
public sealed class ServerProcess : IDisposable
{
    private static readonly string[] ScrubbedVariables =
    [
        "PEAK_MEDIA_OPENROUTER_API_KEY",
        "TTS_OPENROUTER_API_KEY",
        "OpenRouter__ApiKey",
        "Broker__UpstreamClientSecret",
        "Broker__UpstreamAuthority",
        "Broker__UpstreamClientId",
        "McpAuth__Enabled",
        "McpAuth__Resource",
        "Storage__DatabasePath",
        "ASPNETCORE_URLS",
        "ASPNETCORE_ENVIRONMENT",
        "DOTNET_ENVIRONMENT",
        "PEAKMEDIA_TEST_STORAGE_FAULT",
    ];

    private readonly Process _process;
    private readonly List<string> _stdout = [];
    private readonly List<string> _stderr = [];
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _stdoutClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stderrClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ServerProcess(Process process)
    {
        _process = process;
    }

    public static ServerProcess Start(string project, IEnumerable<string> args, IDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoPaths.RepoRoot,
        };
        startInfo.ArgumentList.Add(RepoPaths.ServerDll(project));
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var name in ScrubbedVariables)
        {
            startInfo.Environment.Remove(name);
        }

        foreach (var (name, value) in environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var server = new ServerProcess(process);
        process.OutputDataReceived += (_, e) => server.Append(e.Data, server._stdout, server._stdoutClosed);
        process.ErrorDataReceived += (_, e) => server.Append(e.Data, server._stderr, server._stderrClosed);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return server;
    }

    public IReadOnlyList<string> StdoutLines
    {
        get
        {
            lock (_gate)
            {
                return [.. _stdout];
            }
        }
    }

    public IReadOnlyList<string> StderrLines
    {
        get
        {
            lock (_gate)
            {
                return [.. _stderr];
            }
        }
    }

    public string Stderr => string.Join('\n', StderrLines);

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public async Task WriteLineAsync(string line)
    {
        await _process.StandardInput.WriteLineAsync(line);
        await _process.StandardInput.FlushAsync();
    }

    public void CloseStdin()
    {
        _process.StandardInput.Close();
    }

    /// <summary>Waits for the process to exit and for both output streams to drain.</summary>
    public async Task<int> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await _process.WaitForExitAsync(cts.Token);
        await Task.WhenAll(_stdoutClosed.Task, _stderrClosed.Task).WaitAsync(cts.Token);
        return _process.ExitCode;
    }

    public async Task<string> WaitForStdoutAsync(Func<string, bool> match, TimeSpan timeout)
    {
        return await WaitForLineAsync(() => StdoutLines, match, timeout, "standard output");
    }

    public async Task<string> WaitForStderrAsync(Regex pattern, TimeSpan timeout)
    {
        return await WaitForLineAsync(() => StderrLines, pattern.IsMatch, timeout, "standard error");
    }

    /// <summary>Stops the process (SIGKILL) and waits for the output streams to drain.</summary>
    public async Task StopAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        await WaitForExitAsync(TimeSpan.FromSeconds(15));
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
    }

    private void Append(string? line, List<string> target, TaskCompletionSource closed)
    {
        if (line is null)
        {
            closed.TrySetResult();
            return;
        }

        lock (_gate)
        {
            target.Add(line);
        }
    }

    private async Task<string> WaitForLineAsync(Func<IReadOnlyList<string>> lines, Func<string, bool> match, TimeSpan timeout, string stream)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hit = lines().FirstOrDefault(match);
            if (hit is not null)
            {
                return hit;
            }

            if (_process.HasExited && _stdoutClosed.Task.IsCompleted && _stderrClosed.Task.IsCompleted)
            {
                break;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"No matching line on {stream} within {timeout}. stderr:\n{Stderr}");
    }
}
