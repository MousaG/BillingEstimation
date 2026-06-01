using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ElectricityConsumptionForecasting.Api.Controllers;

[ApiController]
[Route("api/forecast/monthly")]
public sealed class ForecastController : ControllerBase
{
    private readonly IEnhancedSimilarPatternForecastEngine forecastEngine;
    private readonly IForecastBatchService batchService;
    private readonly IForecastQueryService queryService;
    private readonly IForecastRequestValidator requestValidator;

    public ForecastController(
        IEnhancedSimilarPatternForecastEngine forecastEngine,
        IForecastBatchService batchService,
        IForecastQueryService queryService,
        IForecastRequestValidator requestValidator)
    {
        this.forecastEngine = forecastEngine;
        this.batchService = batchService;
        this.queryService = queryService;
        this.requestValidator = requestValidator;
    }

    [HttpPost("customer")]
    public async Task<ActionResult<ForecastResponse>> ForecastCustomer([FromBody] ForecastCustomerRequest request, CancellationToken cancellationToken)
    {
        var validation = requestValidator.Validate(request);
        if (!validation.IsValid)
        {
            return BadRequest(new { errors = validation.Errors });
        }

        return Ok(await forecastEngine.ForecastAsync(request, cancellationToken: cancellationToken));
    }

    [HttpPost("run-batch")]
    public async Task<ActionResult<BatchForecastResponse>> RunBatch([FromBody] BatchForecastRequest request, CancellationToken cancellationToken)
    {
        var validation = requestValidator.Validate(request);
        if (!validation.IsValid)
        {
            return BadRequest(new { errors = validation.Errors });
        }

        return Ok(await batchService.RunBatchAsync(request, cancellationToken));
    }

    [HttpGet("result/{billIdentifier}/{year:int}/{month:int}")]
    public async Task<ActionResult<ForecastResponse>> GetResult(string billIdentifier, int year, int month, CancellationToken cancellationToken)
    {
        var result = await queryService.GetResultAsync(billIdentifier, year, month, cancellationToken);
        return result is null ? NotFound(new { error = "Forecast result was not found." }) : Ok(result);
    }

    [HttpGet("dashboard")]
    public Task<ForecastDashboardSummary> Dashboard([FromQuery] int coCode, [FromQuery] int year, [FromQuery] int month, CancellationToken cancellationToken) =>
        queryService.GetDashboardSummaryAsync(coCode, year, month, cancellationToken);
}
