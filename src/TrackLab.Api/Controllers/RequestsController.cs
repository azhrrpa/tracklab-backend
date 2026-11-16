using Microsoft.AspNetCore.Mvc;
using TrackLab.Api.Audit;
using TrackLab.Api.Contracts;
using TrackLab.Api.Upstream;

namespace TrackLab.Api.Controllers;

/// <summary>Lab analysis requests (t_request_headers).</summary>
[Route("api/requests")]
public sealed class RequestsController(UpstreamGateway gateway, AuditTrailRecorder audit, ILogger<RequestsController> logger)
    : ResourceControllerBase(gateway, audit, logger)
{
    protected override string ResourcePath => "requests";

    protected override string EntityName => "request";

    /// <summary>All tracking steps recorded for one request.</summary>
    [HttpGet("{id:guid}/trackings")]
    public Task<IActionResult> GetTrackings(Guid id, CancellationToken cancellationToken) =>
        ForwardAsync(new UpstreamRequest(HttpMethod.Get, UpstreamArea.Feature, $"trackings/request-id/{id}"), cancellationToken);

    [HttpPost]
    public Task<IActionResult> Create(CreateRequestRequest body, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var payload = new
        {
            partnerId = body.PartnerId,
            projectCode = body.ProjectCode.Trim(),
            totalFee = body.TotalFee,
            lastStatus = Clean(body.LastStatus),
            receivingStatus = Clean(body.ReceivingStatus),
            leadTime = body.LeadTime,
            receivedDate = body.ReceivedDate,
            analysisWipDate = body.AnalysisWipDate,
            analysisCompletedDate = body.AnalysisCompletedDate,
            id,
            createdBy = CurrentUser,
        };

        return CreateAsync(payload, id, cancellationToken);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateRequestRequest body, CancellationToken cancellationToken)
    {
        var payload = new
        {
            partnerId = body.PartnerId,
            projectCode = body.ProjectCode.Trim(),
            totalFee = body.TotalFee,
            lastStatus = Clean(body.LastStatus),
            receivingStatus = Clean(body.ReceivingStatus),
            leadTime = body.LeadTime,
            receivedDate = body.ReceivedDate,
            analysisWipDate = body.AnalysisWipDate,
            analysisCompletedDate = body.AnalysisCompletedDate,
            id,
            updatedBy = CurrentUser,
            isActive = body.IsActive,
        };

        return UpdateAsync(payload, id, cancellationToken);
    }
}
