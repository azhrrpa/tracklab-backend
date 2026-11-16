using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackLab.Api.Audit;

[JsonConverter(typeof(JsonStringEnumConverter<AuditAction>))]
public enum AuditAction
{
    Create,
    Update,
    Delete,
    HardDelete,
}

/// <summary>One changed field: what it was before and what it became.</summary>
public sealed record AuditChange(string Field, string? OldValue, string? NewValue);

/// <summary>An immutable record of who changed what, and when.</summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; init; }

    public required string User { get; init; }

    public required AuditAction Action { get; init; }

    /// <summary>tracker, request or tracking.</summary>
    public required string Entity { get; init; }

    public required string EntityId { get; init; }

    public IReadOnlyList<AuditChange> Changes { get; init; } = [];

    /// <summary>The record as it was before the change (from the API), when available.</summary>
    public JsonElement? Before { get; init; }

    /// <summary>The data that was sent to the API.</summary>
    public JsonElement? After { get; init; }

    public string? CorrelationId { get; init; }

    public string? ClientIp { get; init; }

    public string? UserAgent { get; init; }
}

public sealed class AuditQuery
{
    public string? Entity { get; set; }

    public string? EntityId { get; set; }

    public string? User { get; set; }

    public AuditAction? Action { get; set; }

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; } = 20;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);

public interface IAuditTrailStore
{
    Task AddAsync(AuditEntry entry, CancellationToken cancellationToken);

    Task<PagedResult<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken);

    Task<AuditEntry?> FindAsync(Guid id, CancellationToken cancellationToken);
}
