using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TrackLab.Api.Audit;
using TrackLab.Api.Contracts;
using TrackLab.Api.Upstream;

namespace TrackLab.Api.Controllers;

/// <summary>Master trackers (m_trackers): the list of steps a request can go through.</summary>
[Route("api/trackers")]
public sealed class TrackersController(UpstreamGateway gateway, AuditTrailRecorder audit, ILogger<TrackersController> logger)
    : ResourceControllerBase(gateway, audit, logger)
{
    protected override string ResourcePath => "trackers";

    protected override string EntityName => "tracker";

    [HttpGet("code/{code}")]
    public Task<IActionResult> GetByCode(
        [RegularExpression("^[A-Za-z0-9][A-Za-z0-9_.-]{0,49}$")] string code,
        CancellationToken cancellationToken) =>
        ForwardAsync(
            new UpstreamRequest(HttpMethod.Get, UpstreamArea.Feature, $"trackers/code/{Uri.EscapeDataString(code)}"),
            cancellationToken);

    [HttpPost]
    public Task<IActionResult> Create(CreateTrackerRequest body, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var payload = new
        {
            code = body.Code.Trim(),
            parentCode = Clean(body.ParentCode),
            description = body.Description.Trim(),
            actionType = body.ActionType,
            moduleFlow = body.ModuleFlow.Trim(),
            isUsingNotification = body.IsUsingNotification,
            isPublic = body.IsPublic,
            icon = Clean(body.Icon),
            order = body.Order,
            mandays = body.Mandays,
            isParallel = body.IsParallel,
            id,
            createdBy = CurrentUser,
        };

        return CreateAsync(payload, id, cancellationToken);
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateTrackerRequest body, CancellationToken cancellationToken)
    {
        var payload = new
        {
            code = body.Code.Trim(),
            parentCode = Clean(body.ParentCode),
            description = body.Description.Trim(),
            actionType = body.ActionType,
            moduleFlow = body.ModuleFlow.Trim(),
            isUsingNotification = body.IsUsingNotification,
            isPublic = body.IsPublic,
            icon = Clean(body.Icon),
            order = body.Order,
            mandays = body.Mandays,
            isParallel = body.IsParallel,
            id,
            updatedBy = CurrentUser,
            isActive = body.IsActive,
        };

        return UpdateAsync(payload, id, cancellationToken);
    }
}
