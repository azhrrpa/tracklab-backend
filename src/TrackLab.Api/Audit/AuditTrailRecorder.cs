using System.Text.Json;

namespace TrackLab.Api.Audit;

/// <summary>
/// Builds and saves audit entries. A failure to write the audit trail is logged but never undoes
/// or fails the user's change, which has already been saved by the API.
/// </summary>
public sealed class AuditTrailRecorder(IAuditTrailStore store, TimeProvider time, ILogger<AuditTrailRecorder> logger)
{
    public async Task RecordAsync(
        HttpContext context,
        string entity,
        string entityId,
        AuditAction action,
        JsonElement? before,
        JsonElement? after,
        CancellationToken cancellationToken)
    {
        var user = context.User.Identity?.Name ?? "unknown";

        try
        {
            var entry = new AuditEntry
            {
                Timestamp = time.GetUtcNow(),
                User = user,
                Action = action,
                Entity = entity,
                EntityId = entityId,
                Before = AuditDiff.Unwrap(before),
                After = after,
                Changes = action == AuditAction.Update || action == AuditAction.Create
                    ? AuditDiff.Compute(before, after)
                    : [],
                CorrelationId = context.TraceIdentifier,
                ClientIp = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent
                    ? agent[..Math.Min(agent.Length, 300)]
                    : null,
            };

            await store.AddAsync(entry, cancellationToken);
            logger.LogInformation(
                "Audit: {User} performed {AuditAction} on {Entity} {EntityId} ({ChangeCount} field changes)",
                user, action, entity, entityId, entry.Changes.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to write audit entry for {AuditAction} on {Entity} {EntityId} by {User}", action, entity, entityId, user);
        }
    }
}
