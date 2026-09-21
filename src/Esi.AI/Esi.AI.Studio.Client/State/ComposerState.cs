using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>State for composing a chat message and optional image.</summary>
public sealed class ComposerState
{
    public string Content { get; set; } = string.Empty;
    public ChatImage? SelectedImage { get; set; }
    public string? SelectedImageName { get; set; }
    public string? ImagePreviewUrl { get; set; }
}