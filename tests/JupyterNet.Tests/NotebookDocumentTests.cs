using JupyterNet.Engine;

namespace JupyterNet.Tests;

public class NotebookDocumentTests
{
    [Fact]
    public void EmptyJsonProducesNoCells()
    {
        var document = NotebookDocument.Parse("");
        Assert.Empty(document.Cells);
    }

    [Fact]
    public void RoundTripsCodeAndMarkdownCellsWithPerCellLanguage()
    {
        var original = new NotebookDocument();
        original.Cells.Add(new NotebookCell { Index = 0, Kind = "markdown", Source = "# Title\nsome text" });
        original.Cells.Add(new NotebookCell { Index = 1, Kind = "code", Language = "csharp", Source = "var x = 1;\nx + 1" });
        original.Cells.Add(new NotebookCell { Index = 2, Kind = "code", Language = "fsharp", Source = "let x = 1" });

        var roundTripped = NotebookDocument.Parse(original.ToJson());

        Assert.Equal(3, roundTripped.Cells.Count);
        Assert.Equal("markdown", roundTripped.Cells[0].Kind);
        Assert.Equal("# Title\nsome text", roundTripped.Cells[0].Source);
        Assert.Equal("code", roundTripped.Cells[1].Kind);
        Assert.Equal("csharp", roundTripped.Cells[1].Language);
        Assert.Equal("var x = 1;\nx + 1", roundTripped.Cells[1].Source);
        Assert.Equal("fsharp", roundTripped.Cells[2].Language);
    }

    [Fact]
    public void CodeCellWithoutLanguageMetadataDefaultsToCSharp()
    {
        const string json = """
            { "cells": [ { "cell_type": "code", "source": ["1 + 1"], "metadata": {} } ], "nbformat": 4, "nbformat_minor": 5 }
            """;
        var document = NotebookDocument.Parse(json);
        Assert.Equal("csharp", document.Cells[0].Language);
    }

    [Fact]
    public void CodeCellsOnlyExcludesMarkdown()
    {
        var document = new NotebookDocument();
        document.Cells.Add(new NotebookCell { Index = 0, Kind = "markdown", Source = "# hi" });
        document.Cells.Add(new NotebookCell { Index = 1, Kind = "code", Language = "csharp", Source = "1" });

        Assert.Single(document.CodeCells);
        Assert.Equal(1, document.CodeCells.First().Index);
    }

    [Fact]
    public void RoundTripsDisplayDataAndErrorOutputs()
    {
        var original = new NotebookDocument();
        var cell = new NotebookCell { Index = 0, Kind = "code", Language = "csharp", Source = "1" };
        cell.Outputs.Add(NotebookOutput.Display("text/plain", "42"));
        cell.Outputs.Add(NotebookOutput.Display("text/html", "<b>hi</b>"));
        cell.Outputs.Add(NotebookOutput.Error("boom", "at Foo.Bar()"));
        original.Cells.Add(cell);

        var roundTripped = NotebookDocument.Parse(original.ToJson());
        var outputs = roundTripped.Cells[0].Outputs;

        Assert.Equal(3, outputs.Count);
        Assert.Equal("display_data", outputs[0].OutputType);
        Assert.Equal("42", outputs[0].Data!["text/plain"]);
        Assert.Equal("<b>hi</b>", outputs[1].Data!["text/html"]);
        Assert.Equal("error", outputs[2].OutputType);
        Assert.Equal("boom", outputs[2].ErrorMessage);
        Assert.Equal("at Foo.Bar()", outputs[2].ErrorTraceback);
    }
}
