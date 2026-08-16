namespace Game.Bots.Llm.Tests;

/// <summary>Проверяет CSV-файл метрик (запрос пользователя 2026-08-16) без единого обращения к LLM.</summary>
public sealed class BotMetricsLogTests
{
    [Fact]
    public void Record_WritesHeaderThenCsvRow()
    {
        var writer = new StringWriter();
        var metrics = new BotMetricsLog(writer);

        metrics.Record("Команда А", 3, TimeSpan.FromMilliseconds(1234), 5678, "buildFactory(iron-mine)");

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("bot,turn,response_time_ms,request_size_bytes,command", lines[0]);
        Assert.Equal("Команда А,3,1234,5678,buildFactory(iron-mine)", lines[1]);
    }

    [Fact]
    public void Record_EscapesFieldsContainingComma()
    {
        var writer = new StringWriter();
        var metrics = new BotMetricsLog(writer);

        metrics.Record("bot", 1, TimeSpan.Zero, 0, "buildFactory(iron-mine, recipe=ore-mining)");

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("bot,1,0,0,\"buildFactory(iron-mine, recipe=ore-mining)\"", lines[1]);
    }

    [Fact]
    public void Record_EscapesEmbeddedQuotes()
    {
        var writer = new StringWriter();
        var metrics = new BotMetricsLog(writer);

        metrics.Record("bot \"nickname\"", 1, TimeSpan.Zero, 0, "nop");

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("\"bot \"\"nickname\"\"\",1,0,0,nop", lines[1]);
    }

    [Fact]
    public void Record_NegativeRequestSize_Throws()
    {
        var metrics = new BotMetricsLog(new StringWriter());

        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.Record("bot", 1, TimeSpan.Zero, -1, "nop"));
    }

    [Fact]
    public void Create_WritesHeaderOnceAcrossReopens()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bot-metrics-{Ulid.NewUlid()}.csv");
        try
        {
            using (var first = BotMetricsLog.Create(path))
            {
                first.Record("bot", 1, TimeSpan.FromSeconds(1), 100, "nop");
            }

            using (var second = BotMetricsLog.Create(path))
            {
                second.Record("bot", 2, TimeSpan.FromSeconds(2), 200, "takeLoan(500)");
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal("bot,turn,response_time_ms,request_size_bytes,command", lines[0]);
            Assert.Equal(3, lines.Length); // header + 2 rows, no duplicated header
            Assert.Equal("bot,1,1000,100,nop", lines[1]);
            Assert.Equal("bot,2,2000,200,takeLoan(500)", lines[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
