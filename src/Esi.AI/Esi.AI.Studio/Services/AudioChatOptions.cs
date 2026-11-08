namespace Esi.AI.Studio.Services;

/// <summary>Configures the external Whisper.cpp and Piper speech runtimes.</summary>
public sealed class AudioChatOptions
{
    public const string SectionName = "AudioChat";

    public string WhisperExecutablePath { get; set; } = "whisper-cli";
    public string WhisperModelPath { get; set; } = string.Empty;
    public string PiperExecutablePath { get; set; } = "piper";
    public string PiperModelPath { get; set; } = string.Empty;
    public int SampleRate { get; set; } = 16000;
    public int VoiceThresholdRms { get; set; } = 550;
    public int EndOfSpeechSilenceMilliseconds { get; set; } = 700;
    public int PreRollMilliseconds { get; set; } = 200;
    public int MinimumSpeechMilliseconds { get; set; } = 250;
    public int MaximumUtteranceSeconds { get; set; } = 30;
    public int MaximumChunkBytes { get; set; } = 32 * 1024;
    public int BufferedChunkCapacity { get; set; } = 32;
    public int PlaybackAcknowledgementTimeoutSeconds { get; set; } = 120;
}

internal sealed record AudioRuntimeComponentStatus(bool IsReady, string? Issue);

internal static class AudioRuntimePathResolver
{
    public static string? ResolveExecutable(string? executablePath, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        if (Path.IsPathRooted(executablePath) ||
            executablePath.Contains(Path.DirectorySeparatorChar) ||
            executablePath.Contains(Path.AltDirectorySeparatorChar))
        {
            var resolvedPath = Path.IsPathRooted(executablePath)
                ? executablePath
                : Path.Combine(contentRootPath, executablePath);
            return File.Exists(resolvedPath) ? Path.GetFullPath(resolvedPath) : null;
        }

        var pathEntries = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in pathEntries)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, executablePath + extension);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public static string? ResolveModel(string? modelPath, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            return null;

        var resolvedPath = Path.IsPathRooted(modelPath)
            ? modelPath
            : Path.Combine(contentRootPath, modelPath);
        return File.Exists(resolvedPath) ? Path.GetFullPath(resolvedPath) : null;
    }
}
