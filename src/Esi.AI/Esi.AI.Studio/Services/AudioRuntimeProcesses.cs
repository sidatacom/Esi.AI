using System.Diagnostics;
using Microsoft.Extensions.Options;
using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

internal interface IAudioTranscriber
{
    AudioRuntimeComponentStatus GetStatus();
    Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm16Mono, CancellationToken cancellationToken);
}

internal interface IAudioSpeechSynthesizer
{
    AudioRuntimeComponentStatus GetStatus();
    Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken);
}

internal sealed class WhisperCppTranscriber(
    IOptions<AudioChatOptions> options,
    IHostEnvironment environment,
    ILogger<WhisperCppTranscriber> logger) : IAudioTranscriber
{
    public AudioRuntimeComponentStatus GetStatus()
    {
        var executable = AudioRuntimePathResolver.ResolveExecutable(options.Value.WhisperExecutablePath, environment.ContentRootPath);
        if (executable is null)
            return new(false, "whisper-cli was not found. Configure AudioChat:WhisperExecutablePath.");

        var model = AudioRuntimePathResolver.ResolveModel(options.Value.WhisperModelPath, environment.ContentRootPath);
        return model is null
            ? new(false, "The Whisper model file was not found. Configure AudioChat:WhisperModelPath.")
            : new(true, null);
    }

    public async Task<string> TranscribeAsync(ReadOnlyMemory<byte> pcm16Mono, CancellationToken cancellationToken)
    {
        var status = GetStatus();
        if (!status.IsReady)
            throw new InvalidOperationException(status.Issue);
        if (pcm16Mono.IsEmpty || pcm16Mono.Length % sizeof(short) != 0)
            throw new ArgumentException("Whisper input must contain non-empty PCM16 audio.", nameof(pcm16Mono));

        var executable = AudioRuntimePathResolver.ResolveExecutable(options.Value.WhisperExecutablePath, environment.ContentRootPath)!;
        var model = AudioRuntimePathResolver.ResolveModel(options.Value.WhisperModelPath, environment.ContentRootPath)!;
        var temporaryDirectory = CreateTemporaryDirectory();
        var inputPath = Path.Combine(temporaryDirectory, "utterance.wav");
        var outputPrefix = Path.Combine(temporaryDirectory, "transcript");
        var outputPath = $"{outputPrefix}.txt";

        try
        {
            var wave = PcmAudio.CreateWave(pcm16Mono.Span, options.Value.SampleRate);
            await File.WriteAllBytesAsync(inputPath, wave, cancellationToken).ConfigureAwait(false);

            var startInfo = CreateProcessStartInfo(executable);
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(model);
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add("de");
            startInfo.ArgumentList.Add("-otxt");
            startInfo.ArgumentList.Add("-of");
            startInfo.ArgumentList.Add(outputPrefix);

            var result = await AudioProcessRunner.RunAsync(startInfo, null, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new AudioRuntimeException($"whisper-cli exited with code {result.ExitCode}: {result.StandardError}");
            if (!File.Exists(outputPath))
                throw new AudioRuntimeException("whisper-cli completed without producing its transcript file.");

            return (await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false)).Trim();
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "esi-ai-audio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private void DeleteTemporaryDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not remove temporary Whisper audio directory {Path}.", path);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Could not remove temporary Whisper audio directory {Path}.", path);
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(string executable) => new(executable)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
}

internal sealed class PiperTtsSynthesizer(
    IOptions<AudioChatOptions> options,
    IHostEnvironment environment,
    ILogger<PiperTtsSynthesizer> logger) : IAudioSpeechSynthesizer
{
    public AudioRuntimeComponentStatus GetStatus()
    {
        var executable = AudioRuntimePathResolver.ResolveExecutable(options.Value.PiperExecutablePath, environment.ContentRootPath);
        if (executable is null)
            return new(false, "piper was not found. Configure AudioChat:PiperExecutablePath.");

        var model = AudioRuntimePathResolver.ResolveModel(options.Value.PiperModelPath, environment.ContentRootPath);
        return model is null
            ? new(false, "The Piper voice model file was not found. Configure AudioChat:PiperModelPath.")
            : new(true, null);
    }

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var status = GetStatus();
        if (!status.IsReady)
            throw new InvalidOperationException(status.Issue);

        var executable = AudioRuntimePathResolver.ResolveExecutable(options.Value.PiperExecutablePath, environment.ContentRootPath)!;
        var model = AudioRuntimePathResolver.ResolveModel(options.Value.PiperModelPath, environment.ContentRootPath)!;
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "esi-ai-audio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var outputPath = Path.Combine(temporaryDirectory, "speech.wav");

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(model);
            startInfo.ArgumentList.Add("--output_file");
            startInfo.ArgumentList.Add(outputPath);

            var result = await AudioProcessRunner.RunAsync(startInfo, text, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new AudioRuntimeException($"piper exited with code {result.ExitCode}: {result.StandardError}");
            if (!File.Exists(outputPath))
                throw new AudioRuntimeException("piper completed without producing its WAV output.");

            var audio = await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
            if (audio.Length < 44 || !audio.AsSpan(0, 4).SequenceEqual("RIFF"u8))
                throw new AudioRuntimeException("piper produced an invalid WAV file.");

            return audio;
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Could not remove temporary Piper audio directory {Path}.", temporaryDirectory);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning(exception, "Could not remove temporary Piper audio directory {Path}.", temporaryDirectory);
            }
        }
    }
}

internal sealed record AudioProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class AudioRuntimeException(string message) : InvalidOperationException(message);

internal static class AudioProcessRunner
{
    public static async Task<AudioProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new AudioRuntimeException($"Could not start audio runtime '{startInfo.FileName}'.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteLineAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);
        return new AudioProcessResult(process.ExitCode, standardOutput, standardError.Trim());
    }
}
