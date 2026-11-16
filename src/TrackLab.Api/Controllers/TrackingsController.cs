using Microsoft.AspNetCore.Mvc;
using TrackLab.Api.Audit;
using TrackLab.Api.Contracts;
using TrackLab.Api.Upstream;

namespace TrackLab.Api.Controllers;

/// <summary>Tracking records (t_trackings): which tracker steps a request has reached.</summary>
[Route("api/trackings")]
public sealed class TrackingsController(UpstreamGateway gateway, AuditTrailRecorder audit, ILogger<TrackingsController> logger)
    : ResourceControllerBase(gateway, audit, logger)
{
    protected override string ResourcePath => "trackings";

    protected override string EntityName => "tracking";

    [HttpPost]
    public Task<IActionResult> Create(CreateTrackingRequest body, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var payload = new
        {
            trackerCode = Clean(body.TrackerCode),
            value = Clean(body.Value),
            trackerId = body.TrackerId,
            requestId = body.RequestId,
            partnerId = body.PartnerId,
            id,
            createdBy = CurrentUser,
        };

        return CreateAsync(payload, id, cancellationToken);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateTrackingRequest body, CancellationToken cancellationToken)
    {
        var payload = new
        {
            trackerCode = Clean(body.TrackerCode),
            value = Clean(body.Value),
            trackerId = body.TrackerId,
            requestId = body.RequestId,
            partnerId = body.PartnerId,
            id,
            updatedBy = CurrentUser,
            isActive = body.IsActive,
        };

        return UpdateAsync(payload, id, cancellationToken);
    }
}
