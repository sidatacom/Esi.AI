using System.Diagnostics;

namespace Esi.AI.PyTorch;

/// <summary>Runs a local Python process and forwards its output lines.</summary>
public interface IPythonTrainingProcessRunner
{
    /// <summary>Starts a process with the supplied arguments and waits for its exit.</summary>
    /// <param name="startInfo">The executable and argument list.</param>
    /// <param name="outputHandler">The handler for standard output and standard error lines.</param>
    /// <param name="cancellationToken">A token that terminates the process tree.</param>
    /// <returns>The process exit code.</returns>
    Task<int> RunAsync(ProcessStartInfo startInfo, Func<string, Task> outputHandler, CancellationToken cancellationToken);
}

/// <summary>Implements cancellable process execution for the Python training worker.</summary>
public sealed class PythonTrainingProcessRunner : IPythonTrainingProcessRunner
{
    /// <inheritdoc />
    public async Task<int> RunAsync(ProcessStartInfo startInfo, Func<string, Task> outputHandler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(outputHandler);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("The Python training process could not be started.");

        var standardOutputTask = ForwardLinesAsync(process.StandardOutput, outputHandler, cancellationToken);
        var standardErrorTask = ForwardLinesAsync(process.StandardError, outputHandler, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static async Task ForwardLinesAsync(
        StreamReader reader,
        Func<string, Task> outputHandler,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            await outputHandler(line).ConfigureAwait(false);
    }
}