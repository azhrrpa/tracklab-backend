using Microsoft.AspNetCore.Mvc;
using TrackLab.Api.Audit;

namespace TrackLab.Api.Controllers;

/// <summary>Read-only access to the audit trail: who created, changed or deleted what, and when.</summary>
[ApiController]
[Route("api/audit-trails")]
[Produces("application/json")]
public sealed class AuditTrailsController(IAuditTrailStore store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditEntry>>> Get([FromQuery] AuditQuery query, CancellationToken cancellationToken) =>
        Ok(await store.QueryAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuditEntry>> GetById(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) is { } entry
            ? Ok(entry)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "Audit entry not found.");
}
