using NETRA.Application.BulkImport;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Verifies the dependency-free CSV reader used by bulk import: RFC 4180 quoting,
/// line-ending folding, byte-order handling and the record/character ceilings that keep
/// a hostile file from making the server allocate without bound. The reader is pure C# in
/// the application layer, which is where CSV parsing belongs: the browser never interprets
/// a file.
/// </summary>
public sealed class CsvReaderTests
{
    private static CsvReadResult Read(string text, int maxRecords = 100, int maxRawChars = 4_000) =>
        CsvReader.Read(
            new StringReader(text),
            new CsvReadLimits { MaxRecords = maxRecords, MaxRawChars = maxRawChars });

    [Fact]
    public void ReadsSimpleRows_IncludingTrailingLineBreak()
    {
        var result = Read("A,B\n1,2\n3,4\n");

        result.Succeeded.Should().BeTrue();
        result.Records.Should().HaveCount(3);
        result.Records[1].Fields.Should().Equal("1", "2");
        result.Records[2].Fields.Should().Equal("3", "4");
    }

    [Fact]
    public void DoesNotInventAPhantomRecordForATrailingNewline()
    {
        Read("A,B\n").Records.Should().HaveCount(1);
        Read("A,B").Records.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("A,B\r\n1,2\r\n")]
    [InlineData("A,B\n1,2\n")]
    [InlineData("A,B\r1,2\r")]
    public void FoldsEveryCommonLineEndingTheSameWay(string text)
    {
        var result = Read(text);

        result.Succeeded.Should().BeTrue();
        result.Records.Should().HaveCount(2);
        result.Records[1].Fields.Should().Equal("1", "2");
    }

    [Fact]
    public void ResolvesQuotedFieldsAndEscapedQuotes()
    {
        var result = Read("\"Hello, world\",\"He said \"\"hi\"\"\",plain");

        result.Succeeded.Should().BeTrue();
        result.Records[0].Fields.Should().Equal("Hello, world", "He said \"hi\"", "plain");
    }

    [Fact]
    public void KeepsNewlinesInsideQuotedValuesInOneRecord()
    {
        var result = Read("A,B\n\"line one\nline two\",2\n");

        result.Succeeded.Should().BeTrue();
        result.Records.Should().HaveCount(2);
        result.Records[1].Fields[0].Should().Contain("line one");
    }

    [Fact]
    public void FlattensNewlinesInStoredRawTextSoItStaysSingleLine()
    {
        var result = Read("A,B\n\"line one\nline two\",2\n");

        var raw = result.Records[1].Raw;
        raw.Should().NotContain("\n");
        CsvReader.ReadSingleRecord(raw)!.Fields.Should().Equal(result.Records[1].Fields);
    }

    [Fact]
    public void PreservesEmptyAndWhitespaceOnlyFields()
    {
        var result = Read("A,,C\n , , \n");

        result.Records[0].Fields.Should().Equal("A", "", "C");
        result.Records[1].Fields.Should().Equal(" ", " ", " ");
    }

    [Fact]
    public void StripsALeadingByteOrderMarkFromTheFirstHeader()
    {
        var result = Read("﻿CaseNumber,Title\nCC-1,Robbery\n");

        result.Succeeded.Should().BeTrue();
        result.Records[0].Fields[0].Should().Be("CaseNumber");
    }

    [Fact]
    public void FailsWhenAFileEndsInsideAQuotedValue()
    {
        var result = Read("A,B\n\"never closed,2");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("ends inside a quoted value");
    }

    [Fact]
    public void FailsWhenTextFollowsAClosingQuote()
    {
        var result = Read("A,B\n\"value\"junk,2");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("unexpected characters");
    }

    [Fact]
    public void StopsAtTheRecordCeilingInsteadOfGrowingWithoutLimit()
    {
        var text = string.Concat(Enumerable.Repeat("a,b\n", 40));

        var result = Read(text, maxRecords: 10);

        result.LimitExceeded.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("more than 10 records");
        result.Records.Should().BeEmpty();
    }

    [Fact]
    public void CapsRetainedRowTextWithoutRefusingTheRow()
    {
        var longValue = new string('x', 500);

        var result = Read($"A,B\n{longValue},2", maxRawChars: 50);

        result.Succeeded.Should().BeTrue();
        result.Records[1].Raw.Should().HaveLength(50);
        result.Records[1].RawTruncated.Should().BeTrue();
        result.Records[1].Fields[0].Should().HaveLength(500);
    }

    [Fact]
    public void WriteRecordQuotesOnlyWhatNeedsIt()
    {
        CsvReader.WriteRecord(["plain", "a,b", "say \"hi\"", "two\nlines"])
            .Should().Be("plain,\"a,b\",\"say \"\"hi\"\"\",\"two\nlines\"");
    }

    [Fact]
    public void ReadSingleRecordReturnsNullForBlankInput()
    {
        CsvReader.ReadSingleRecord(string.Empty).Should().BeNull();
    }

    [Fact]
    public void ReadSingleRecordRoundTripsWhatWasWritten()
    {
        var fields = new string?[] { "CC-1", "Title, with comma", "", "12.5" };

        var parsed = CsvReader.ReadSingleRecord(CsvReader.WriteRecord(fields));

        parsed!.Fields.Should().Equal(fields);
    }

    [Fact]
    public void NumberingAndLineTrackingPointAtTheFile()
    {
        var result = Read("A,B\n1,2\n\"multi\nline\",3\n");

        result.Records[0].Number.Should().Be(1);
        result.Records[0].StartLine.Should().Be(1);
        result.Records[2].Number.Should().Be(3);
        result.Records[2].StartLine.Should().Be(3);
    }
}
