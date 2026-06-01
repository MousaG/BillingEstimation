namespace ElectricityConsumptionForecasting.Domain.Entities;

public sealed class ForecastSimilarSubscriber
{
    public long Id { get; set; }
    public long ForecastResultId { get; set; }
    public ForecastResult? ForecastResult { get; set; }
    public string SimilarBillIdentifier { get; set; } = string.Empty;
    public decimal SimilarityScore { get; set; }
    public decimal ConsumptionSimilarity { get; set; }
    public decimal TrendSimilarity { get; set; }
    public decimal SeasonalSimilarity { get; set; }
    public decimal ProfileSimilarity { get; set; }
    public decimal GeographicSimilarity { get; set; }
    public decimal SimilarMonthConsumption { get; set; }
    public decimal Weight { get; set; }
    public bool IsOutlier { get; set; }
}
