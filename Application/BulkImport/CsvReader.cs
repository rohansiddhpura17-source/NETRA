using System.Text;

namespace NETRA.Application.BulkImport;

/// <summary>
/// One record read from a delimited text file. Fields keep the file's own spelling
/// (quotes resolved, doubled quotes unescaped); trimming is left to the caller so an
/// unexpectedly blank value stays visible to validation.
/// </summary>
public sealed class CsvRecord
{
    internal CsvRecord(int number, int startLine, string[] fields, string raw, bool rawTruncated)
    {
        Number = number;
        StartLine = startLine;
        Fields = fields;
        Raw = raw;
        RawTruncated = rawTruncated;
    }

    /// <summary>Ordinal in the record stream, 1-based. The header row is record 1.</summary>
    public int Number { get; }

    /// <summary>Physical line the record starts on, so messages can point at the file.</summary>
    public int StartLine { get; }

    public IReadOnlyList<string> Fields { get; }

    /// <summary>
    /// Single-line text of the record, suitable for storing and for re-parsing. Line
    /// breaks inside quoted values become spaces.
    /// </summary>
    public string Raw { get; }

    /// <summary>True when <see cref="Raw"/> was cut short and can no longer be re-parsed.</summary>
    public bool RawTruncated { get; }
}

/// <summary>Bounds applied while reading. Every allocation is bounded by these values.</summary>
public sealed class CsvReadLimits
{
    /// <summary>Read at most this many records, then stop instead of growing without limit.</summary>
    public int MaxRecords { get; init; } = 5_000;

    /// <summary>Per-record character budget for the retained raw text.</summary>
    public int MaxRawChars { get; init; } = 4_000;
}

/// <summary>Outcome of reading a file: the records, or why reading stopped.</summary>
public sealed class CsvReadResult
{
    private CsvReadResult(IReadOnlyList<CsvRecord> records, string? error, bool limitExceeded)
    {
        Records = records;
        Error = error;
        LimitExceeded = limitExceeded;
    }

    public IReadOnlyList<CsvRecord> Records { get; }

    /// <summary>File-level defect that makes the whole import unusable. Null when reading succeeded.</summary>
    public string? Error { get; }

    /// <summary>True when the record budget was exceeded.</summary>
    public bool LimitExceeded { get; }

    public bool Succeeded => Error is null && !LimitExceeded;

    public static CsvReadResult Failure(string error) =>
        new(Array.Empty<CsvRecord>(), error, false);

    public static CsvReadResult OverLimit(string error) =>
        new(Array.Empty<CsvRecord>(), error, true);

    internal static CsvReadResult Ok(IReadOnlyList<CsvRecord> records) =>
        new(records, null, false);
}

/// <summary>
/// Dependency-free reader for comma-separated files (RFC 4180 quoting rules). CSV
/// parsing is a backend responsibility: the browser only posts a file and never
/// interprets it.
/// </summary>
/// <remarks>
/// The reader works on one character of lookahead at a time, so an oversized or
/// deeply malformed file cannot make the process allocate beyond the configured
/// limits. Records are always materialised without executing anything they contain.
/// </remarks>
public static class CsvReader
{
    public const char Delimiter = ',';

    private const int EndOfFile = -1;

    /// <summary>Leading byte-order mark, which must not become part of the first header name.</summary>
    private const int ByteOrderMark = 0xFEFF;

    private const char Quote = '"';

    public static CsvReadResult Read(TextReader reader, CsvReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        limits ??= new CsvReadLimits();

        var cursor = new CharCursor(reader);
        SkipByteOrderMark(cursor);

        var records = new List<CsvRecord>();
        var field = new StringBuilder();
        var raw = new StringBuilder();

        while (true)
        {
            // A record starts only where characters remain, so a trailing line break ends
            // the file instead of becoming a phantom row.
            if (cursor.Peek() == EndOfFile)
            {
                break;
            }

            var startLine = cursor.Line;
            var fields = new List<string>();
            raw.Clear();
            var rawTruncated = false;

            while (true)
            {
                field.Clear();

                if (cursor.Peek() == Quote)
                {
                    var quoteResult = ReadQuotedField(cursor, field, raw, limits.MaxRawChars, ref rawTruncated);
                    if (quoteResult is not null)
                    {
                        return CsvReadResult.Failure(quoteResult);
                    }

                    var afterQuote = cursor.Peek();
                    if (afterQuote is not EndOfFile and not '\n' and not Delimiter)
                    {
                        return CsvReadResult.Failure(
                            $"Line {startLine}: a quoted value is followed by unexpected characters.");
                    }
                }
                else
                {
                    while (true)
                    {
                        var ch = cursor.Peek();
                        if (ch is EndOfFile or '\n' or Delimiter)
                        {
                            break;
                        }

                        cursor.Take();
                        AppendRaw(raw, ch, limits.MaxRawChars, ref rawTruncated);
                        field.Append((char)ch);
                    }
                }

                fields.Add(field.ToString());

                var next = cursor.Peek();
                if (next == Delimiter)
                {
                    cursor.Take();
                    AppendRaw(raw, Delimiter, limits.MaxRawChars, ref rawTruncated);
                    continue;
                }

                if (next == '\n')
                {
                    cursor.Take();
                }

                break;
            }

            // A blank line inside the data is still a row: validation reports it rather
            // than silently renumbering the rows below it.
            records.Add(new CsvRecord(records.Count + 1, startLine, fields.ToArray(), raw.ToString(), rawTruncated));

            if (records.Count > limits.MaxRecords)
            {
                return CsvReadResult.OverLimit(
                    $"The file contains more than {limits.MaxRecords:N0} records.");
            }
        }

        return CsvReadResult.Ok(records);
    }

    /// <summary>
    /// Re-parses a single stored record line. Used by the preview page, which must show
    /// the staged rows without touching the case tables.
    /// </summary>
    public static CsvRecord? ReadSingleRecord(string rawLine, int maxRawChars = 4_000)
    {
        if (string.IsNullOrEmpty(rawLine))
        {
            return null;
        }

        var result = Read(new StringReader(rawLine), new CsvReadLimits { MaxRecords = 1, MaxRawChars = maxRawChars });
        return result.Succeeded && result.Records.Count > 0 ? result.Records[0] : null;
    }

    /// <summary>
    /// Writes fields back as one CSV record. Values that contain the delimiter, a quote
    /// or a line break are quoted, so stored row text stays faithful to the upload.
    /// </summary>
    public static string WriteRecord(IReadOnlyList<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var builder = new StringBuilder();

        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(Delimiter);
            }

            var value = fields[index] ?? string.Empty;
            var needsQuoting = value.Contains(Delimiter)
                               || value.Contains(Quote)
                               || value.Contains('\n')
                               || value.Contains('\r');

            if (!needsQuoting)
            {
                builder.Append(value);
                continue;
            }

            builder.Append(Quote).Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append(Quote);
        }

        return builder.ToString();
    }

    private static string? ReadQuotedField(
        CharCursor cursor,
        StringBuilder field,
        StringBuilder raw,
        int maxRawChars,
        ref bool rawTruncated)
    {
        cursor.Take();
        AppendRaw(raw, Quote, maxRawChars, ref rawTruncated);

        while (true)
        {
            var ch = cursor.Peek();

            if (ch == EndOfFile)
            {
                return "The file ends inside a quoted value, so the last row is incomplete.";
            }

            cursor.Take();

            if (ch == Quote)
            {
                AppendRaw(raw, ch, maxRawChars, ref rawTruncated);

                if (cursor.Peek() != Quote)
                {
                    return null;
                }

                // Doubled quote is an escaped quote character.
                cursor.Take();
                AppendRaw(raw, Quote, maxRawChars, ref rawTruncated);
                field.Append(Quote);
                continue;
            }

            // Line breaks inside a record are legal, but the stored text is single-line.
            char literal = ch == '\n' ? ' ' : (char)ch;
            AppendRaw(raw, literal, maxRawChars, ref rawTruncated);
            field.Append(literal);
        }
    }

    private static void AppendRaw(StringBuilder raw, int ch, int maxRawChars, ref bool rawTruncated)
    {
        if (raw.Length >= maxRawChars)
        {
            rawTruncated = true;
            return;
        }

        raw.Append((char)ch);
    }

    private static void SkipByteOrderMark(CharCursor cursor)
    {
        if (cursor.Peek() == ByteOrderMark)
        {
            cursor.Take();
        }
    }
    /// <summary>
    /// Character source with one character of lookahead. Carriage returns are folded into
    /// line feeds so CR, LF and CRLF files all read the same way.
    /// </summary>
    private sealed class CharCursor(TextReader reader)
    {
        private const int Nothing = -2;

        private int _buffered = Nothing;

        public int Line { get; private set; } = 1;

        public int Peek()
        {
            if (_buffered == Nothing)
            {
                _buffered = Normalize();
            }

            return _buffered;
        }

        public int Take()
        {
            var value = Peek();
            _buffered = Nothing;

            if (value == '\n')
            {
                Line++;
            }

            return value;
        }

        private int Normalize()
        {
            var value = reader.Read();

            if (value != '\r')
            {
                return value;
            }

            if (reader.Peek() == '\n')
            {
                reader.Read();
            }

            return '\n';
        }
    }
}
