using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using HackTK.Next.Abstractions;

namespace HackTK.Next.Diagnostics;

public sealed class StructuredDiagnosticLogger : IDiagnosticLogger, IDisposable
{
    private readonly ITimeProvider _timeProvider;
    private readonly int _maxRetainedEvents;
    private readonly ConcurrentQueue<DiagnosticEvent> _events = new();
    private readonly BlockingCollection<string>? _fileQueue;
    private readonly Thread? _writerThread;
    private int _retainedCount;
    private long _droppedLines;
    private bool _disposed;

    public StructuredDiagnosticLogger(ITimeProvider timeProvider, int maxRetainedEvents = 2048, string? tsvFilePath = null)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (maxRetainedEvents <= 0) throw new ArgumentOutOfRangeException(nameof(maxRetainedEvents));
        _maxRetainedEvents = maxRetainedEvents;
        TsvFilePath = tsvFilePath;

        if (!string.IsNullOrWhiteSpace(tsvFilePath))
        {
            string fullPath = Path.GetFullPath(tsvFilePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _fileQueue = new BlockingCollection<string>(boundedCapacity: 4096);
            _writerThread = new Thread(() => RunFileWriter(stream, _fileQueue))
            {
                IsBackground = true,
                Name = "HackTK.Next.DiagnosticsWriter"
            };
            _writerThread.Start();
        }
    }

    public string? TsvFilePath { get; }
    public long DroppedLines => Interlocked.Read(ref _droppedLines);
    public string WriterError { get; private set; } = string.Empty;

    public void Record(string category, string message, IReadOnlyDictionary<string, string>? fields = null)
    {
        if (_disposed) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        var copy = fields is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(fields, StringComparer.Ordinal);

        var entry = new DiagnosticEvent(_timeProvider.UtcNow, category, message ?? string.Empty, copy);
        _events.Enqueue(entry);
        int current = Interlocked.Increment(ref _retainedCount);
        while (current > _maxRetainedEvents && _events.TryDequeue(out _))
        {
            current = Interlocked.Decrement(ref _retainedCount);
        }

        if (_fileQueue is not null && !_fileQueue.IsAddingCompleted)
        {
            string line = FormatTsvLine(entry);
            try
            {
                if (!_fileQueue.TryAdd(line))
                    Interlocked.Increment(ref _droppedLines);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref _droppedLines);
            }
        }
    }

    public IReadOnlyList<DiagnosticEvent> GetRecentEvents() => _events.ToArray();

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _retainedCount);
        }
    }

    public static string FormatTsvLine(DiagnosticEvent entry)
    {
        var sb = new StringBuilder();
        sb.Append(entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
        sb.Append('\t');
        sb.Append(Sanitize(entry.Category));
        sb.Append('\t');
        sb.Append(Sanitize(entry.Message));
        sb.Append('\t');

        bool first = true;
        foreach (var pair in entry.Fields)
        {
            if (!first) sb.Append(';');
            first = false;
            sb.Append(Sanitize(pair.Key));
            sb.Append('=');
            sb.Append(Sanitize(pair.Value));
        }

        return sb.ToString();
    }

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private void RunFileWriter(FileStream stream, BlockingCollection<string> queue)
    {
        try
        {
            using (stream)
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.AutoFlush = true;
                writer.WriteLine("utc\tcategory\tmessage\tfields; schema=3; engine=HackTK.Next");
                foreach (string line in queue.GetConsumingEnumerable())
                {
                    if (stream.Position < 32L * 1024L * 1024L)
                        writer.WriteLine(line);
                    else
                        Interlocked.Increment(ref _droppedLines);
                }
                writer.WriteLine($"END\tdropped={DroppedLines}");
            }
        }
        catch (Exception ex)
        {
            WriterError = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_fileQueue is not null)
        {
            _fileQueue.CompleteAdding();
            bool joined = _writerThread is null || _writerThread.Join(millisecondsTimeout: 1500);
            if (joined)
            {
                _fileQueue.Dispose();
            }
        }
    }
}
