using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace TrackLab.Api.Logging;

public sealed class FileLoggingOptions
{
    public const string SectionName = "FileLogging";

    public string Directory { get; set; } = "logs";

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public int RetainedDays { get; set; } = 14;
}

/// <summary>One line in the JSON log file.</summary>
public sealed record LogRecord(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message,
    string? Exception,
    string? CorrelationId,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>
/// Structured logging to daily JSON-lines files (logs/tracklab-YYYYMMDD.jsonl), one JSON object per line.
/// Log calls only enqueue the record; a background task does the file I/O so requests are never slowed down.
/// </summary>
[ProviderAlias("JsonFile")]
public sealed class JsonFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FileLoggingOptions _options;
    private readonly Channel<LogRecord> _queue = Channel.CreateBounded<LogRecord>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly ConcurrentDictionary<string, JsonFileLogger> _loggers = new();
    private readonly Task _writerTask;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public JsonFileLoggerProvider(IOptions<FileLoggingOptions> options)
    {
        _options = options.Value;
        System.IO.Directory.CreateDirectory(_options.Directory);
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public static string FilePathFor(string directory, DateOnly date) =>
        Path.Combine(directory, $"tracklab-{date:yyyyMMdd}.jsonl");

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new JsonFileLogger(name, this));

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _writerTask.Wait(TimeSpan.FromSeconds(3));
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _options.MinimumLevel;

    internal IExternalScopeProvider ScopeProvider => _scopeProvider;

    internal void Enqueue(LogRecord record) => _queue.Writer.TryWrite(record);

    private async Task WriteLoopAsync()
    {
        StreamWriter? writer = null;
        DateOnly? currentDate = null;

        try
        {
            await foreach (var record in _queue.Reader.ReadAllAsync())
            {
                var date = DateOnly.FromDateTime(record.Timestamp.LocalDateTime);
                if (writer is null || currentDate != date)
                {
                    if (writer is not null)
                    {
                        await writer.DisposeAsync();
                    }

                    writer = OpenWriter(date);
                    currentDate = date;
                    DeleteExpiredFiles(date);
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(record, JsonOptions));

                // Flush when the queue is drained, so the file is up to date without flushing every line.
                if (!_queue.Reader.TryPeek(out _))
                {
                    await writer.FlushAsync();
                }
            }
        }
        catch (Exception ex)
        {
            // Logging must never crash the app; report to stderr and stop file logging.
            Console.Error.WriteLine($"File logger stopped: {ex}");
        }
        finally
        {
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }
        }
    }

    private StreamWriter OpenWriter(DateOnly date)
    {
        var stream = new FileStream(
            FilePathFor(_options.Directory, date),
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
        return new StreamWriter(stream);
    }

    private void DeleteExpiredFiles(DateOnly today)
    {
        if (_options.RetainedDays <= 0)
        {
            return;
        }

        var oldest = today.AddDays(-_options.RetainedDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(_options.Directory, "tracklab-*.jsonl"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file).Replace("tracklab-", string.Empty);
            if (DateOnly.TryParseExact(stamp, "yyyyMMdd", out var fileDate) && fileDate < oldest)
            {
                File.Delete(file);
            }
        }
    }
}

internal sealed class JsonFileLogger(string category, JsonFileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        provider.ScopeProvider.Push(state);

    public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var properties = new Dictionary<string, object?>();
        string? correlationId = null;

        provider.ScopeProvider.ForEachScope(
            (scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    foreach (var (key, value) in pairs)
                    {
                        if (key == "CorrelationId")
                        {
                            correlationId = value?.ToString();
                        }
                    }
                }
            },
            state);

        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (var (key, value) in values)
            {
                if (key != "{OriginalFormat}")
                {
                    properties[key] = ToSerializable(value);
                }
            }
        }

        provider.Enqueue(new LogRecord(
            DateTimeOffset.Now,
            logLevel.ToString(),
            category,
            formatter(state, exception),
            exception?.ToString(),
            correlationId,
            properties));
    }

    private static object? ToSerializable(object? value) => value switch
    {
        null => null,
        string or bool or int or long or double or decimal or float or short or byte => value,
        DateTime or DateTimeOffset or Guid => value,
        _ => value.ToString(),
    };
}
