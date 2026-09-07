namespace Esi.AI.Studio.Data;

public sealed class ApplicationSettingsEntity
{
    public int Id { get; set; } = 1;

    public string InferenceTimeoutsJson { get; set; } = "[]";

    public DateTime UpdatedAtUtc { get; set; }
}