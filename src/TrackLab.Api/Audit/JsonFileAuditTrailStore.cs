using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TrackLab.Api.Audit;

public sealed class AuditTrailOptions
{
    public const string SectionName = "AuditTrail";

    public string FilePath { get; set; } = "data/audit-trail.jsonl";
}

/// <summary>
/// Append-only audit trail in a JSON-lines file: entries are only ever added, never edited or removed,
/// which is what an audit trail needs. A lock keeps concurrent writes from interleaving.
/// To move to a database later, implement IAuditTrailStore and swap the registration in Program.cs.
/// </summary>
public sealed class JsonFileAuditTrailStore : IAuditTrailStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonFileAuditTrailStore(IOptions<AuditTrailOptions> options)
    {
        _filePath = options.Value.FilePath;
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task AddAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_filePath, line, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PagedResult<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        var entries = await ReadAllAsync(cancellationToken);

        var filtered = entries
            .Where(e => query.Entity is null || string.Equals(e.Entity, query.Entity, StringComparison.OrdinalIgnoreCase))
            .Where(e => query.EntityId is null || string.Equals(e.EntityId, query.EntityId, StringComparison.OrdinalIgnoreCase))
            .Where(e => query.User is null || e.User.Contains(query.User, StringComparison.OrdinalIgnoreCase))
            .Where(e => query.Action is null || e.Action == query.Action)
            .Where(e => query.From is null || e.Timestamp >= query.From)
            .Where(e => query.To is null || e.Timestamp <= query.To)
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, 100);
        return new PagedResult<AuditEntry>(filtered.Skip(offset).Take(limit).ToList(), filtered.Count, offset, limit);
    }

    public async Task<AuditEntry?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await ReadAllAsync(cancellationToken)).FirstOrDefault(e => e.Id == id);

    public void Dispose() => _lock.Dispose();

    private async Task<List<AuditEntry>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
            var entries = new List<AuditEntry>(lines.Length);
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<AuditEntry>(line, JsonOptions);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // Skip a corrupted line instead of losing the whole trail.
                }
            }

            return entries;
        }
        finally
        {
            _lock.Release();
        }
    }
}
