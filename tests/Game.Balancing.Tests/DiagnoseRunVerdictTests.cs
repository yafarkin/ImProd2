namespace Game.Balancing.Tests;

/// <summary>
/// <see cref="DiagnoseRun.PrintFinalVerdict"/> — регрессия на находку 2026-08-23 (синтетическая
/// 6-уровневая цепочка, ускорили разблокировку поколений): §1c (<see
/// cref="TeamSteadyStateCalculator"/>) пессимистично считает потолок вложений в поколение/R&amp;D
/// вечным расходом, хотя по факту он прекращается после разблокировки — из-за этого §1c может
/// формально не сходиться, даже когда динамический идеальный зал (§3) уже здоров. Вердикт не должен
/// блокировать партию в этом случае — только предупреждать; блокировать обязан, только если ОБЕ
/// проверки (§1c и §3) согласны, что дело плохо.
/// </summary>
public class DiagnoseRunVerdictTests
{
    private static string CaptureVerdict(
        IReadOnlyList<TeamSteadyStateCalculator.SectorSteadyState> badSteadyStates,
        IReadOnlyDictionary<string, DiagnoseRun.ChainVerdict> idealVerdicts)
    {
        var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            DiagnoseRun.PrintFinalVerdict(
                costAnomalies: [],
                badPayback: [],
                supplyDeficits: [],
                badSteadyStates: badSteadyStates,
                sandwich: new DiagnoseRun.MarginSandwichResult(true, "ok"),
                idealVerdicts: idealVerdicts,
                averageScoreBySector: idealVerdicts.Keys.ToDictionary(id => id, _ => 5m));
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    private static TeamSteadyStateCalculator.SectorSteadyState BadSectorA() => new()
    {
        SectorId = "A",
        ProfitPerTurn = 1m,
        SalaryPerTurn = 2m,
        GenerationResearchPerTurn = 3m,
        RndPerTurn = 0m,
        FactoryCount = 1,
        RndCeilingPerFactory = 0m,
    };

    [Fact]
    public void Does_Not_Block_When_Steady_State_Fails_But_Ideal_Hall_Is_Healthy()
    {
        var idealVerdicts = new Dictionary<string, DiagnoseRun.ChainVerdict>
        {
            ["A"] = new DiagnoseRun.ChainVerdict(IsPositive: true, IsRecovering: true, Label: "здоровая"),
        };

        var output = CaptureVerdict([BadSectorA()], idealVerdicts);

        Assert.DoesNotContain("ИГРАТЬ НЕЛЬЗЯ", output);
        Assert.Contains("⚠", output);
        Assert.Contains("Не блокирует вердикт", output);
    }

    [Fact]
    public void Still_Blocks_When_Both_Steady_State_And_Ideal_Hall_Fail()
    {
        var idealVerdicts = new Dictionary<string, DiagnoseRun.ChainVerdict>
        {
            ["A"] = new DiagnoseRun.ChainVerdict(IsPositive: false, IsRecovering: false, Label: "монотонно падает"),
        };

        var output = CaptureVerdict([BadSectorA()], idealVerdicts);

        Assert.Contains("❌ ИГРАТЬ НЕЛЬЗЯ", output);
    }
}
