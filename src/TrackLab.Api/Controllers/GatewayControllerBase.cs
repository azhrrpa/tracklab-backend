using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TrackLab.Api.Audit;
using TrackLab.Api.Contracts;
using TrackLab.Api.Upstream;

namespace TrackLab.Api.Controllers;

/// <summary>Shared plumbing: send a request upstream as the current user and turn the response into an HTTP result.</summary>
[ApiController]
[Produces("application/json")]
public abstract class GatewayControllerBase(UpstreamGateway gateway, ILogger logger) : ControllerBase
{
    protected UpstreamGateway Gateway => gateway;

    protected string CurrentUser => User.Identity?.Name ?? "unknown";

    protected async Task<IActionResult> ForwardAsync(UpstreamRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await gateway.SendAsUserAsync(HttpContext, request, cancellationToken));

    protected IActionResult ToActionResult(UpstreamResponse response)
    {
        foreach (var (name, value) in response.ForwardHeaders)
        {
            Response.Headers[name] = value;
        }

        if (response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Session expired",
                detail: "Your session has expired. Please log in again.");
        }

        if (response.StatusCode >= 500)
        {
            logger.LogError(
                "Upstream returned {StatusCode}: {BodySnippet}",
                response.StatusCode,
                response.Body.Length > 500 ? response.Body[..500] : response.Body);

            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Upstream error",
                detail: "The assessment API returned an error. Please try again in a moment.");
        }

        if (string.IsNullOrWhiteSpace(response.Body))
        {
            return StatusCode(response.StatusCode);
        }

        var content = IsJson(response)
            ? response.Body
            : JsonSerializer.Serialize(new { message = response.Body });

        return new ContentResult
        {
            StatusCode = response.StatusCode,
            Content = content,
            ContentType = "application/json; charset=utf-8",
        };
    }

    protected static JsonElement? TryParseJson(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsJson(UpstreamResponse response)
    {
        if (response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var trimmed = response.Body.TrimStart();
        return (trimmed.StartsWith('{') || trimmed.StartsWith('[')) && TryParseJson(trimmed) is not null;
    }
}

/// <summary>
/// The endpoints every resource (trackers, requests, trackings) shares: paging, advanced search,
/// list all, get by id and delete. Writes are recorded in the audit trail.
/// </summary>
public abstract class ResourceControllerBase(UpstreamGateway gateway, AuditTrailRecorder audit, ILogger logger)
    : GatewayControllerBase(gateway, logger)
{
    /// <summary>Path segment on the assessment API, e.g. "trackers".</summary>
    protected abstract string ResourcePath { get; }

    /// <summary>Name used in the audit trail, e.g. "tracker".</summary>
    protected abstract string EntityName { get; }

    /// <summary>Paged list. Paging values go upstream as X-Paging-* headers; totals come back in the same style.</summary>
    [HttpGet]
    public Task<IActionResult> GetPage([FromQuery] PagingQuery query, CancellationToken cancellationToken) =>
        ForwardAsync(
            new UpstreamRequest(HttpMethod.Get, UpstreamArea.Feature, $"{ResourcePath}/paging")
            {
                Headers = PagingHeaders.FromSimple(query),
            },
            cancellationToken);

    /// <summary>Paged list with include/exclude filters, e.g. include={"last_status":"REQ-ACC||REQ-CREATE"}.</summary>
    [HttpGet("advanced")]
    public Task<IActionResult> GetAdvancedPage([FromQuery] AdvancedPagingQuery query, CancellationToken cancellationToken) =>
        ForwardAsync(
            new UpstreamRequest(HttpMethod.Get, UpstreamArea.Feature, $"{ResourcePath}/paging/advance")
            {
                Headers = PagingHeaders.FromAdvanced(query),
            },
            cancellationToken);

    /// <summary>Every record, unpaged. Used for dropdowns.</summary>
    [HttpGet("all")]
    public Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        ForwardAsync(new UpstreamRequest(HttpMethod.Get, UpstreamArea.Feature, ResourcePath), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        ForwardAsync(new UpstreamRequest(HttpMethod.Get, UpstreamArea.Feature, $"{ResourcePath}/{id}"), cancellationToken);

    /// <summary>Soft delete by default (is_active = false); pass hard=true to remove the row permanently.</summary>
    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, [FromQuery] bool hard, CancellationToken cancellationToken) =>
        WriteWithAuditAsync(
            id,
            hard ? AuditAction.HardDelete : AuditAction.Delete,
            new UpstreamRequest(HttpMethod.Delete, UpstreamArea.Feature, ResourcePath)
            {
                Body = new { id, deletedBy = CurrentUser, doHardDelete = hard },
            },
            payload: null,
            cancellationToken);

    protected Task<IActionResult> CreateAsync(object payload, Guid id, CancellationToken cancellationToken) =>
        WriteWithAuditAsync(
            id,
            AuditAction.Create,
            new UpstreamRequest(HttpMethod.Post, UpstreamArea.Feature, ResourcePath) { Body = payload },
            payload,
            cancellationToken);

    protected Task<IActionResult> UpdateAsync(object payload, Guid id, CancellationToken cancellationToken) =>
        WriteWithAuditAsync(
            id,
            AuditAction.Update,
            new UpstreamRequest(HttpMethod.Put, UpstreamArea.Feature, ResourcePath) { Body = payload },
            payload,
            cancellationToken);

    private async Task<IActionResult> WriteWithAuditAsync(
        Guid id,
        AuditAction action,
        UpstreamRequest request,
        object? payload,
        CancellationToken cancellationToken)
    {
        // Snapshot the record first so the audit trail can show what changed.
        JsonElement? before = null;
        if (action != AuditAction.Create)
        {
            var current = await Gateway.SendAsUserAsync(
                HttpContext,
                new UpstreamRequest(HttpMethod.Get, UpstreamArea.Feature, $"{ResourcePath}/{id}"),
                cancellationToken);

            if (current.StatusCode == StatusCodes.Status404NotFound)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: $"The {EntityName} no longer exists.");
            }

            before = current.IsSuccess ? TryParseJson(current.Body) : null;
        }

        var response = await Gateway.SendAsUserAsync(HttpContext, request, cancellationToken);

        if (response.IsSuccess)
        {
            var entityId = action == AuditAction.Create
                ? TokenExtractor.FindString(response.Body, "id") ?? id.ToString()
                : id.ToString();

            var after = payload is null
                ? (JsonElement?)null
                : JsonSerializer.SerializeToElement(payload, payload.GetType(), UpstreamApiClient.JsonOptions);

            await audit.RecordAsync(HttpContext, EntityName, entityId, action, before, after, cancellationToken);
            Response.Headers["X-Resource-Id"] = entityId;
        }

        return ToActionResult(response);
    }

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
