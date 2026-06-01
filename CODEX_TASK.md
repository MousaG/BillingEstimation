# Codex Task: Enhanced Similar Pattern Billing Estimation Engine

## Goal
Build a production-ready monthly electricity consumption forecasting engine for household subscribers. The engine estimates monthly consumption when real monthly readings are unavailable, supports billing workflows, and identifies subscribers that should be routed to expert review.

The algorithm is based on the Enhanced Similar Pattern approach inspired by the article `Forecasting household monthly electricity consumption using the similar pattern algorithm`.

## Target Stack
- Backend: ASP.NET Core Web API
- Language: C#
- Database: SQL Server
- ORM: Entity Framework Core
- Architecture: Clean Architecture or at least clear layering:
  - Domain
  - Application
  - Infrastructure
  - Api
- Testing: xUnit

## Solution Name
`ElectricityConsumptionForecasting`

## Main Business Requirements
1. Forecast monthly household electricity consumption for a target subscriber and target month.
2. Use real smart-meter or real reading data when available; do not forecast when valid actual reading exists.
3. Use the subscriber's previous 4 to 12 months of valid consumption history.
4. Select similar subscribers using climate, tariff, geography, phase, ampere, meter type, consumption band, and consumption pattern.
5. Calculate a composite similarity score.
6. Remove outliers from candidate similar subscribers.
7. Produce predicted consumption using weighted averaging.
8. Produce a confidence score and confidence level.
9. Mark low-confidence or abnormal subscribers as not forecastable or requiring expert review.
10. Store forecast results, details, similar subscribers, warnings, and run metadata.
11. Support both online API forecasting for one subscriber and batch forecasting for many subscribers.

## API Output Example
```json
{
  "billIdentifier": "1234567890123",
  "targetYear": 1403,
  "targetMonth": 8,
  "predictedConsumption": 186.4,
  "confidence": 0.84,
  "confidenceLevel": "High",
  "similarSubscribersCount": 52,
  "outliersRemoved": 4,
  "method": "EnhancedSimilarPattern",
  "isForecastable": true,
  "riskLevel": "Low",
  "requiresExpertReview": false,
  "reason": "Similar consumption pattern found in same climate, tariff, phase and consumption band",
  "warnings": []
}
```

## Required Projects
Create a .NET solution with these projects:

```text
src/ElectricityConsumptionForecasting.Api
src/ElectricityConsumptionForecasting.Application
src/ElectricityConsumptionForecasting.Domain
src/ElectricityConsumptionForecasting.Infrastructure
tests/ElectricityConsumptionForecasting.Tests
```

## Domain Entities
Implement these entities:

### CustomerProfile
Fields:
- Id
- BillIdentifier
- CoCode
- RegionCode
- CityCode
- VillageCode
- TariffType
- UsageType
- Phase
- Ampere
- ContractDemand
- ClimateType
- IsUrban
- MeterType
- IsSmartMeter
- IsMultiTariff
- InstallDate
- ActivityStatus
- CreatedAt
- UpdatedAt

### CustomerMonthlyConsumption
Fields:
- Id
- BillIdentifier
- Year
- Month
- Consumption
- PeakConsumption
- MidPeakConsumption
- OffPeakConsumption
- FridayConsumption
- ReactiveConsumption
- DaysCount
- ReadingType
- BillType
- HasCorrection
- HasMeterChange
- IsActualReading
- IsSmartReading
- DataQualityStatus
- CreatedAt

### ForecastRun
Fields:
- Id
- CoCode
- TargetYear
- TargetMonth
- StartedAt
- FinishedAt
- Status
- TotalCustomers
- ForecastedCount
- NotForecastableCount
- FailedCount
- ParametersJson

### ForecastResult
Fields:
- Id
- RunId
- BillIdentifier
- CoCode
- TargetYear
- TargetMonth
- PredictedConsumption
- ActualConsumption
- Confidence
- ConfidenceLevel
- SimilarSubscribersCount
- OutliersRemovedCount
- IsForecastable
- RequiresExpertReview
- RiskLevel
- MethodName
- Reason
- CreatedAt

### ForecastSimilarSubscriber
Fields:
- Id
- ForecastResultId
- SimilarBillIdentifier
- SimilarityScore
- ConsumptionSimilarity
- TrendSimilarity
- SeasonalSimilarity
- ProfileSimilarity
- GeographicSimilarity
- SimilarMonthConsumption
- Weight
- IsOutlier

### ForecastWarning
Fields:
- Id
- ForecastResultId
- Code
- Message
- Severity
- CreatedAt

### ForecastConfig
Fields:
- Id
- Name
- Value
- Description
- IsActive
- CreatedAt
- UpdatedAt

### CustomerDataQualityIssue
Fields:
- Id
- BillIdentifier
- Year
- Month
- IssueCode
- IssueDescription
- Severity
- CreatedAt

## Configuration Parameters
All thresholds must be configurable; avoid magic numbers.

Default configuration:
- MinimumHistoryMonths = 4
- MaximumHistoryMonths = 12
- MinimumSimilarSubscribersTemperate = 20
- MinimumSimilarSubscribersTropical = 10
- TopNSimilarSubscribers = 100
- HighConfidenceThreshold = 0.80
- MediumConfidenceThreshold = 0.55
- LowConfidenceThreshold = 0.35
- IqrOutlierMultiplier = 1.5
- MaxAllowedMonthlyGrowthRatio = 3.0
- MaxAllowedMonthlyDropRatio = 0.20
- ConsumptionSimilarityWeight = 0.35
- TrendSimilarityWeight = 0.25
- SeasonalSimilarityWeight = 0.20
- ProfileSimilarityWeight = 0.10
- GeographicSimilarityWeight = 0.10

## Algorithm
Implement `EnhancedSimilarPatternForecastEngine`.

Steps:
1. Receive BillIdentifier, TargetYear, TargetMonth.
2. Load target customer profile.
3. Check for valid actual/smart reading in target month.
   - If actual exists, return it with method `ActualReading` and confidence 1.0.
4. Load valid history for the previous 4 to 12 months.
5. If history is insufficient, return not forecastable with reason `Insufficient valid history`.
6. Select candidate subscribers:
   - Same climate type
   - Same tariff type
   - Prefer same city/region
   - Similar phase/ampere/meter type
   - Active subscribers only
   - Exclude records with correction, meter change, invalid reading, or severe data quality issue
7. Calculate composite similarity score using:
   - ConsumptionLevelSimilarity
   - TrendSimilarity
   - SeasonalSimilarity
   - ProfileSimilarity
   - GeographicSimilarity
8. Select top N candidates above a reasonable similarity threshold.
9. Get their actual consumption for the target month.
10. Remove outliers using IQR.
11. Calculate weighted average prediction.
12. Calculate confidence based on:
   - Number of similar subscribers
   - Similarity score distribution
   - Candidate consumption variance
   - History completeness
   - Data quality
13. Apply billing validation rules.
14. Save result, similar subscribers, warnings.
15. Return API response.

## Similarity Rules
Implement these services:

### IConsumptionSimilarityService
Compare historical consumption vectors.
Use percentage difference and normalized distance.

### ITrendSimilarityService
Compare month-to-month direction and slope.

### ISeasonalSimilarityService
Compare target subscriber with same month in previous years when available.

### IProfileSimilarityService
Compare tariff, phase, ampere, meter type, and usage type.

### IGeographicSimilarityService
Score based on same city, same region, same company, and climate.

## Outlier Detection
Implement IQR in version 1:
- Q1
- Q3
- IQR = Q3 - Q1
- LowerBound = Q1 - 1.5 * IQR
- UpperBound = Q3 + 1.5 * IQR

Keep the design open for DBSCAN later.

## API Endpoints
Implement these endpoints:

### Forecast one customer
`POST /api/forecast/monthly/customer`

Request:
```json
{
  "billIdentifier": "1234567890123",
  "targetYear": 1403,
  "targetMonth": 8,
  "useSmartMeterIfAvailable": true
}
```

### Run batch forecast
`POST /api/forecast/monthly/run-batch`

Request:
```json
{
  "coCode": 141,
  "targetYear": 1403,
  "targetMonth": 8,
  "tariffType": 10,
  "maxCustomers": 100000
}
```

### Get result
`GET /api/forecast/monthly/result/{billIdentifier}/{year}/{month}`

### Dashboard summary
`GET /api/forecast/monthly/dashboard?coCode=141&year=1403&month=8`

## SQL Server Requirements
Create EF Core mappings and migrations or SQL scripts for all tables.

Add indexes:
- CustomerMonthlyConsumption(BillIdentifier, Year, Month)
- CustomerMonthlyConsumption(Year, Month, ReadingType, DataQualityStatus)
- CustomerProfile(CoCode, ClimateType, TariffType, RegionCode, CityCode)
- ForecastResult(BillIdentifier, TargetYear, TargetMonth)
- ForecastResult(CoCode, TargetYear, TargetMonth, IsForecastable)
- ForecastSimilarSubscriber(ForecastResultId)

## Tests
Create xUnit tests for:
1. Actual reading exists: engine returns actual reading.
2. Insufficient history: not forecastable.
3. Enough similar subscribers: forecast is generated.
4. Outliers are removed.
5. Low confidence triggers expert review.
6. Similarity score increases when profiles match.
7. Batch run stores ForecastRun and ForecastResult rows.

## Coding Standards
- Use async APIs.
- Use cancellation tokens.
- Do not hardcode thresholds.
- Avoid SQL injection.
- Avoid loading millions of records into memory.
- Use repository/query services for candidate selection.
- Keep algorithm logic testable outside the API layer.
- Add XML comments for public services.
- Return structured errors and reasons, not generic exceptions.

## First Deliverable
Create the solution skeleton, entities, DbContext, configuration model, core service interfaces, API request/response DTOs, and an initial working implementation of `EnhancedSimilarPatternForecastEngine` using in-memory calculations after EF query selection.

## Second Deliverable
Add optimized SQL/EF candidate selection and batch forecast execution.

## Third Deliverable
Add dashboard endpoint, warning reports, and comparison with actual consumption when later readings arrive.
