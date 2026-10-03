using KrakowOpenData.Infrastructure.Parsing;

namespace KrakowOpenData.Infrastructure.Tests;

public class CsvReaderTests
{
    private static List<CsvRow> Read(string csv) => CsvReader.Read(new StringReader(csv)).ToList();

    [Fact]
    public void Reads_header_and_rows()
    {
        var rows = Read("a,b\n1,2\n3,4\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("3", rows[1]["a"]);
        Assert.Equal("4", rows[1]["b"]);
    }

    [Fact]
    public void Handles_quoted_fields_with_commas_newlines_and_escaped_quotes()
    {
        var rows = Read("name,desc\n\"Rondo, Mogilskie\",\"line1\nline2 \"\"quoted\"\"\"\n");
        var row = Assert.Single(rows);
        Assert.Equal("Rondo, Mogilskie", row["name"]);
        Assert.Equal("line1\nline2 \"quoted\"", row["desc"]);
    }

    [Fact]
    public void Handles_crlf_bom_and_missing_trailing_newline()
    {
        var rows = Read("﻿stop_id,stop_name\r\n1,Bagatela\r\n2,Poczta Główna");
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Has("stop_id"));
        Assert.Equal("Poczta Główna", rows[1]["stop_name"]);
    }

    [Fact]
    public void Empty_and_missing_columns_read_as_null_or_empty()
    {
        var row = Assert.Single(Read("a,b,c\n1,,\n"));
        Assert.Null(row.Get("b"));
        Assert.Equal(string.Empty, row["c"]);
        Assert.Null(row.Get("does_not_exist"));
    }

    [Fact]
    public void Header_names_are_trimmed_and_case_insensitive()
    {
        var row = Assert.Single(Read(" Stop_ID , stop_name\n7,X\n"));
        Assert.Equal("7", row["stop_id"]);
    }

    [Fact]
    public void Blank_lines_are_skipped()
    {
        Assert.Equal(2, Read("a\n1\n\n2\n").Count);
    }
}
