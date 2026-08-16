namespace Game.Bots.Llm.Tests;

/// <summary>Проверяет CSV-файл метрик (запрос пользователя 2026-08-16) без единого обращения к LLM.</summary>
public sealed class BotMetricsLogTests
{
    [Fact]
    public void Record_WritesHeaderThenCsvRow()
    {
        var writer = new StringWriter();
        var metrics = new BotMetricsLog(writer);

        metrics.Record("Команда А", 3, 1, TimeSpan.FromMilliseconds(1234), 5678, "buildFactory(iron-mine)", 1000m, 200m, 2);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("bot,turn,action_index,response_time_ms,request_size_bytes,command,balance,debt,net_worth,factory_count", lines[0]);
        Assert.Equal("Команда А,3,1,1234,5678,buildFactory(iron-mine),1000.00,200.00,800.00,2", lines[1]);
    }

    [Fact]
    public void Record_EscapesFieldsContainingComma()
    {
        var writer = new StringWriter();
        var metrics = new BotMetricsLog(writer);

        metrics.Record("bot", 1, 1, TimeSpan.Zero, 0, "buildFactory(iron-mine, recipe=ore-mining)", 0m, 0m, 0);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("bot,1,1,0,0,\"buildFactory(iron-mine, recipe=ore-mining)\",0.00,0.00,0.00,0", lines[1]);
    }

    [Fact]
    public void Record_EscapesEmbeddedQuotes()
    {
        var writer = new StringWriter();
        var metrics = new BotMetricsLog(writer);

        metrics.Record("bot \"nickname\"", 1, 1, TimeSpan.Zero, 0, "nop", 0m, 0m, 0);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("\"bot \"\"nickname\"\"\",1,1,0,0,nop,0.00,0.00,0.00,0", lines[1]);
    }

    [Fact]
    public void Record_NegativeRequestSize_Throws()
    {
        var metrics = new BotMetricsLog(new StringWriter());

        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.Record("bot", 1, 1, TimeSpan.Zero, -1, "nop", 0m, 0m, 0));
    }

    [Fact]
    public void Record_NetWorth_IsBalanceMinusDebt()
    {
        var writer = new StringWriter();
        var metrics = new BotMetricsLog(writer);

        metrics.Record("bot", 1, 1, TimeSpan.Zero, 0, "nop", 500m, 1200m, 1); // принудительный кредит — долг больше баланса, net worth уходит в минус

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("bot,1,1,0,0,nop,500.00,1200.00,-700.00,1", lines[1]);
    }

    [Fact]
    public void Record_ActionIndex_DistinguishesMultipleActionsInOneTurn()
    {
        // Запрос пользователя 2026-08-16: "важно уметь несколько команд за один ход" — file с
        // ходами должен различать, какое это действие внутри хода, не только сам ход.
        var writer = new StringWriter();
        var metrics = new BotMetricsLog(writer);

        metrics.Record("bot", 5, 1, TimeSpan.Zero, 0, "takeLoan(1000)", 1000m, 1000m, 0);
        metrics.Record("bot", 5, 2, TimeSpan.Zero, 0, "buildFactory(iron-mine)", 1000m, 1000m, 1);
        metrics.Record("bot", 5, 3, TimeSpan.Zero, 0, "nop", 1000m, 1000m, 1);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(4, lines.Length); // header + 3 действия одного и того же хода
        Assert.StartsWith("bot,5,1,", lines[1]);
        Assert.StartsWith("bot,5,2,", lines[2]);
        Assert.StartsWith("bot,5,3,", lines[3]);
    }

    [Fact]
    public void Create_WritesHeaderOnceAcrossReopens()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bot-metrics-{Ulid.NewUlid()}.csv");
        try
        {
            using (var first = BotMetricsLog.Create(path))
            {
                first.Record("bot", 1, 1, TimeSpan.FromSeconds(1), 100, "nop", 0m, 0m, 0);
            }

            using (var second = BotMetricsLog.Create(path))
            {
                second.Record("bot", 2, 1, TimeSpan.FromSeconds(2), 200, "takeLoan(500)", 500m, 500m, 0);
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal("bot,turn,action_index,response_time_ms,request_size_bytes,command,balance,debt,net_worth,factory_count", lines[0]);
            Assert.Equal(3, lines.Length); // header + 2 rows, no duplicated header
            Assert.Equal("bot,1,1,1000,100,nop,0.00,0.00,0.00,0", lines[1]);
            Assert.Equal("bot,2,1,2000,200,takeLoan(500),500.00,500.00,0.00,0", lines[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
