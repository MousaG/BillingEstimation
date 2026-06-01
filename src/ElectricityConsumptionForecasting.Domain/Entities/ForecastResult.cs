namespace ElectricityConsumptionForecasting.Domain.Entities;

public sealed class ForecastResult
{
    public long Id { get; set; }
    public long? RunId { get; set; }
    public ForecastRun? Run { get; set; }
    public string BillIdentifier { get; set; } = string.Empty;
    public int CoCode { get; set; }
    public int TargetYear { get; set; }
    public int TargetMonth { get; set; }
    public decimal? PredictedConsumption { get; set; }
    public decimal? ActualConsumption { get; set; }
    public decimal Confidence { get; set; }
    public string ConfidenceLevel { get; set; } = "Low";
    public int SimilarSubscribersCount { get; set; }
    public int OutliersRemovedCount { get; set; }
    public bool IsForecastable { get; set; }
    public bool RequiresExpertReview { get; set; }
    public string RiskLevel { get; set; } = "High";
    public string MethodName { get; set; } = "EnhancedSimilarPattern";
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ForecastSimilarSubscriber> SimilarSubscribers { get; set; } = [];
    public ICollection<ForecastWarning> Warnings { get; set; } = [];
}
