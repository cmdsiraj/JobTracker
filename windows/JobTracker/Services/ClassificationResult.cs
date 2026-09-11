using System.Text.Json.Serialization;
using JobTracker.Models;

namespace JobTracker.Services;

public sealed class ClassificationResult
{
    public int? Index { get; set; }
    public bool IsJobRelated { get; set; }
    public string Company { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Location { get; set; }
    public string? Source { get; set; }
    public string? NextAction { get; set; }
    public string? Cycle { get; set; }
    public double Confidence { get; set; }

    [JsonIgnore]
    public ApplicationStatus? ApplicationStatus => ApplicationStatusExtensions.FromLlmString(Status);

    public static ClassificationResult NotJobRelated(int? index = null) => new()
    {
        Index = index,
        IsJobRelated = false,
        Company = "",
        Role = "",
        Status = "unknown",
        Confidence = 1.0,
    };
}
