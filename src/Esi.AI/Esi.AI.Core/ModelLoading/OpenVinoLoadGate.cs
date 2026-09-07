namespace Esi.AI.Core.ModelLoading;

/// <summary>
/// Ensures that a process performs no more than one OpenVINO model load at a time.
/// </summary>
public sealed class OpenVinoLoadGate
{
    private int isEntered;

    /// <summary>Gets whether an OpenVINO model load is in progress.</summary>
    public bool IsEntered => Volatile.Read(ref isEntered) != 0;

    /// <summary>Attempts to begin an OpenVINO model load.</summary>
    /// <returns><see langword="true"/> when the caller exclusively owns the load operation.</returns>
    public bool TryEnter() => Interlocked.CompareExchange(ref isEntered, 1, 0) == 0;

    /// <summary>Marks the current OpenVINO model load as complete.</summary>
    public void Exit() => Volatile.Write(ref isEntered, 0);
}