using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class DotLlmState
{
    public string ModelPath { get; set; } = string.Empty;
    public string Device { get; set; } = "cpu";
    public int? Threads { get; set; }
    public DotLlmLoadRequest ToRequest() => new(ModelPath, Device, Threads);
    public void Load(DotLlmLoadRequest request) { ModelPath = request.ModelPath; Device = request.Device; Threads = request.Threads; }
    public void Reset() { Device = "cpu"; Threads = null; }
}