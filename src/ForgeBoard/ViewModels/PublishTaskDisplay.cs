namespace ForgeBoard.ViewModels;

public sealed class PublishTaskDisplay
{
    public string ArtifactId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double PercentComplete { get; set; }
    public bool IsComplete { get; set; }
    public bool IsFailed => Error is not null;
    public bool IsActive => !IsComplete;
    public string? Error { get; set; }
    public string DisplayText
    {
        get
        {
            if (Error is not null)
                return $"Failed: {Error}";
            if (IsComplete && Status == "Cancelled")
                return "Cancelled";
            if (IsComplete)
                return "Published successfully";
            return Status;
        }
    }
}
