namespace Esi.AI.LiteLlm.Shared;

/// <summary>Template-Helfertyp für Wetterbeispielseiten.</summary>
public sealed record WeatherForecast(
    DateOnly Date,
    int TemperatureC,
    int WindSpeed,
    string? Summary);