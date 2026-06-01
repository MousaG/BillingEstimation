CREATE TABLE CustomerProfiles (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerProfiles PRIMARY KEY,
    BillIdentifier nvarchar(32) NOT NULL,
    CoCode int NOT NULL,
    RegionCode int NOT NULL,
    CityCode int NULL,
    VillageCode int NULL,
    TariffType int NOT NULL,
    UsageType int NOT NULL,
    Phase int NOT NULL,
    Ampere decimal(10,2) NOT NULL,
    ContractDemand decimal(18,2) NULL,
    ClimateType int NOT NULL,
    IsUrban bit NOT NULL,
    MeterType int NOT NULL,
    IsSmartMeter bit NOT NULL,
    IsMultiTariff bit NOT NULL,
    InstallDate date NULL,
    ActivityStatus nvarchar(32) NOT NULL,
    CreatedAt datetime2 NOT NULL,
    UpdatedAt datetime2 NULL
);

CREATE TABLE CustomerMonthlyConsumptions (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerMonthlyConsumptions PRIMARY KEY,
    BillIdentifier nvarchar(32) NOT NULL,
    [Year] int NOT NULL,
    [Month] int NOT NULL,
    Consumption decimal(18,3) NOT NULL,
    PeakConsumption decimal(18,3) NULL,
    MidPeakConsumption decimal(18,3) NULL,
    OffPeakConsumption decimal(18,3) NULL,
    FridayConsumption decimal(18,3) NULL,
    ReactiveConsumption decimal(18,3) NULL,
    DaysCount int NOT NULL,
    ReadingType nvarchar(32) NOT NULL,
    BillType nvarchar(32) NOT NULL,
    HasCorrection bit NOT NULL,
    HasMeterChange bit NOT NULL,
    IsActualReading bit NOT NULL,
    IsSmartReading bit NOT NULL,
    DataQualityStatus nvarchar(32) NOT NULL,
    CreatedAt datetime2 NOT NULL
);

CREATE TABLE ForecastRuns (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ForecastRuns PRIMARY KEY,
    CoCode int NOT NULL,
    TargetYear int NOT NULL,
    TargetMonth int NOT NULL,
    StartedAt datetime2 NOT NULL,
    FinishedAt datetime2 NULL,
    Status nvarchar(32) NOT NULL,
    TotalCustomers int NOT NULL,
    ForecastedCount int NOT NULL,
    NotForecastableCount int NOT NULL,
    FailedCount int NOT NULL,
    ParametersJson nvarchar(max) NULL
);

CREATE TABLE ForecastResults (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ForecastResults PRIMARY KEY,
    RunId bigint NULL CONSTRAINT FK_ForecastResults_ForecastRuns_RunId REFERENCES ForecastRuns(Id),
    BillIdentifier nvarchar(32) NOT NULL,
    CoCode int NOT NULL,
    TargetYear int NOT NULL,
    TargetMonth int NOT NULL,
    PredictedConsumption decimal(18,3) NULL,
    ActualConsumption decimal(18,3) NULL,
    Confidence decimal(5,4) NOT NULL,
    ConfidenceLevel nvarchar(32) NOT NULL,
    SimilarSubscribersCount int NOT NULL,
    OutliersRemovedCount int NOT NULL,
    IsForecastable bit NOT NULL,
    RequiresExpertReview bit NOT NULL,
    RiskLevel nvarchar(32) NOT NULL,
    MethodName nvarchar(64) NOT NULL,
    Reason nvarchar(512) NOT NULL,
    IsLatest bit NOT NULL,
    SupersededAt datetime2 NULL,
    CreatedAt datetime2 NOT NULL
);

CREATE TABLE ForecastSimilarSubscribers (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ForecastSimilarSubscribers PRIMARY KEY,
    ForecastResultId bigint NOT NULL CONSTRAINT FK_ForecastSimilarSubscribers_ForecastResults_ForecastResultId REFERENCES ForecastResults(Id) ON DELETE CASCADE,
    SimilarBillIdentifier nvarchar(32) NOT NULL,
    SimilarityScore decimal(5,4) NOT NULL,
    ConsumptionSimilarity decimal(5,4) NOT NULL,
    TrendSimilarity decimal(5,4) NOT NULL,
    SeasonalSimilarity decimal(5,4) NOT NULL,
    ProfileSimilarity decimal(5,4) NOT NULL,
    GeographicSimilarity decimal(5,4) NOT NULL,
    SimilarMonthConsumption decimal(18,3) NOT NULL,
    ComparableYear int NOT NULL,
    ComparableMonth int NOT NULL,
    HistoryStartYear int NOT NULL,
    HistoryStartMonth int NOT NULL,
    HistoryEndYear int NOT NULL,
    HistoryEndMonth int NOT NULL,
    [Weight] decimal(8,6) NOT NULL,
    IsOutlier bit NOT NULL
);

CREATE TABLE ForecastWarnings (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ForecastWarnings PRIMARY KEY,
    ForecastResultId bigint NOT NULL CONSTRAINT FK_ForecastWarnings_ForecastResults_ForecastResultId REFERENCES ForecastResults(Id) ON DELETE CASCADE,
    Code nvarchar(64) NOT NULL,
    [Message] nvarchar(512) NOT NULL,
    Severity nvarchar(32) NOT NULL,
    CreatedAt datetime2 NOT NULL
);

CREATE TABLE ForecastConfigs (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ForecastConfigs PRIMARY KEY,
    [Name] nvarchar(128) NOT NULL,
    [Value] nvarchar(256) NOT NULL,
    [Description] nvarchar(512) NULL,
    IsActive bit NOT NULL,
    CreatedAt datetime2 NOT NULL,
    UpdatedAt datetime2 NULL
);

CREATE TABLE CustomerDataQualityIssues (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerDataQualityIssues PRIMARY KEY,
    BillIdentifier nvarchar(32) NOT NULL,
    [Year] int NOT NULL,
    [Month] int NOT NULL,
    IssueCode nvarchar(64) NOT NULL,
    IssueDescription nvarchar(512) NOT NULL,
    Severity nvarchar(32) NOT NULL,
    CreatedAt datetime2 NOT NULL
);

CREATE UNIQUE INDEX IX_CustomerProfiles_BillIdentifier ON CustomerProfiles(BillIdentifier);
CREATE INDEX IX_CustomerMonthlyConsumption_BillIdentifier_Year_Month ON CustomerMonthlyConsumptions(BillIdentifier, [Year], [Month]);
CREATE INDEX IX_CustomerMonthlyConsumption_Year_Month_ReadingType_DataQualityStatus ON CustomerMonthlyConsumptions([Year], [Month], ReadingType, DataQualityStatus);
CREATE INDEX IX_CustomerProfile_CoCode_ClimateType_TariffType_RegionCode_CityCode ON CustomerProfiles(CoCode, ClimateType, TariffType, RegionCode, CityCode);
CREATE INDEX IX_ForecastResult_BillIdentifier_TargetYear_TargetMonth ON ForecastResults(BillIdentifier, TargetYear, TargetMonth);
CREATE INDEX IX_ForecastResult_BillIdentifier_TargetYear_TargetMonth_IsLatest ON ForecastResults(BillIdentifier, TargetYear, TargetMonth, IsLatest);
CREATE INDEX IX_ForecastResult_CoCode_TargetYear_TargetMonth_IsForecastable ON ForecastResults(CoCode, TargetYear, TargetMonth, IsForecastable);
CREATE INDEX IX_ForecastSimilarSubscriber_ForecastResultId ON ForecastSimilarSubscribers(ForecastResultId);
CREATE UNIQUE INDEX IX_ForecastConfigs_Name ON ForecastConfigs([Name]);
