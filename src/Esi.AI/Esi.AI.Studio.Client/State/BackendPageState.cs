namespace Esi.AI.Studio.Client.State;

/// <summary>Single root state for the backends page.</summary>
public sealed class BackendPageState
{
    public LlamaState Llama { get; } = new();
    public OpenVinoState OpenVino { get; } = new();
    public PythonState Python { get; } = new();
    public DotLlmState DotLlm { get; } = new();
    public ProfileState Profiles { get; } = new();
    public LoadingState Loading { get; } = new();
    public RequirementsState Requirements { get; } = new();
}