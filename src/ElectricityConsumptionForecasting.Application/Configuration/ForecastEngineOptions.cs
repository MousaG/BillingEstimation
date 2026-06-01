namespace ElectricityConsumptionForecasting.Application.Configuration;

public sealed class ForecastEngineOptions
{
    public const string SectionName = "ForecastEngine";

    public int MinimumHistoryMonths { get; set; } = 4;
    public int MaximumHistoryMonths { get; set; } = 12;
    public int MinimumSimilarSubscribersTemperate { get; set; } = 20;
    public int MinimumSimilarSubscribersTropical { get; set; } = 10;
    public int TopNSimilarSubscribers { get; set; } = 100;
    public decimal HighConfidenceThreshold { get; set; } = 0.80m;
    public decimal MediumConfidenceThreshold { get; set; } = 0.55m;
    public decimal LowConfidenceThreshold { get; set; } = 0.35m;
    public decimal IqrOutlierMultiplier { get; set; } = 1.5m;
    public decimal MaxAllowedMonthlyGrowthRatio { get; set; } = 3.0m;
    public decimal MaxAllowedMonthlyDropRatio { get; set; } = 0.20m;
    public decimal ConsumptionSimilarityWeight { get; set; } = 0.35m;
    public decimal TrendSimilarityWeight { get; set; } = 0.25m;
    public decimal SeasonalSimilarityWeight { get; set; } = 0.20m;
    public decimal ProfileSimilarityWeight { get; set; } = 0.10m;
    public decimal GeographicSimilarityWeight { get; set; } = 0.10m;
    public decimal MinimumCandidateSimilarity { get; set; } = 0.35m;
    public decimal ConsumptionBandToleranceRatio { get; set; } = 0.30m;
    public decimal AmpereToleranceRatio { get; set; } = 0.20m;
}
