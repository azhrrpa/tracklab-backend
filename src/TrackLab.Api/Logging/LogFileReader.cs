using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TrackLab.Api.Logging;

public sealed class LogQuery
{
    public DateOnly? Date { get; set; }

    /// <summary>Minimum level: Information, Warning, Error or Critical.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Information;

    public string? Search { get; set; }

    public string? CorrelationId { get; set; }

    public int Limit { get; set; } = 200;
}

/// <summary>Reads back the JSON log files, newest entries first, for the log viewer.</summary>
public sealed class LogFileReader(IOptions<FileLoggingOptions> options, TimeProvider time)
{
    public async Task<IReadOnlyList<LogRecord>> ReadAsync(LogQuery query, CancellationToken cancellationToken)
    {
        var date = query.Date ?? DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var path = JsonFileLoggerProvider.FilePathFor(options.Value.Directory, date);
        if (!File.Exists(path))
        {
            return [];
        }

        var limit = Math.Clamp(query.Limit, 1, 1000);
        var results = new List<LogRecord>();

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var record = TryParse(line);
            if (record is not null && Matches(record, query))
            {
                results.Add(record);
            }
        }

        return results.AsEnumerable().Reverse().Take(limit).ToList();
    }

    private static bool Matches(LogRecord record, LogQuery query)
    {
        if (!Enum.TryParse<LogLevel>(record.Level, out var level) || level < query.MinLevel)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId) && record.CorrelationId != query.CorrelationId)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            return record.Message.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || record.Category.Contains(query.Search, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static LogRecord? TryParse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<LogRecord>(line, JsonFileLoggerProvider.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
