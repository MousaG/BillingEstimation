namespace ElectricityConsumptionForecasting.Domain.Entities;

public sealed class CustomerDataQualityIssue
{
    public long Id { get; set; }
    public string BillIdentifier { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string IssueCode { get; set; } = string.Empty;
    public string IssueDescription { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
