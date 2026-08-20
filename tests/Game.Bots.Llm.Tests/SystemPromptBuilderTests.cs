namespace Game.Bots.Llm.Tests;

/// <summary>Проверяет, что системный промпт (шаг 4 плана LLM-ботов) содержит персону и полный справочник команд — без реального инференса.</summary>
public sealed class SystemPromptBuilderTests
{
    [Fact]
    public void Build_IncludesPersonaText()
    {
        var prompt = SystemPromptBuilder.Build("You are cautious and risk-averse.");

        Assert.Contains("You are cautious and risk-averse.", prompt);
    }

    [Theory]
    [InlineData("nop")]
    [InlineData("buildFactory(")]
    [InlineData("setWorkerCount(")]
    [InlineData("selectRecipe(")]
    [InlineData("setRndCommitment(")]
    [InlineData("setGenerationResearchCommitment(")]
    [InlineData("setOverhaulRequested(")]
    [InlineData("takeLoan(")]
    [InlineData("repayLoan(")]
    [InlineData("sellToSystem(")]
    public void Build_ListsEveryCommand(string mnemonic)
    {
        var prompt = SystemPromptBuilder.Build("persona");

        Assert.Contains(mnemonic, prompt);
    }

    [Fact]
    public void Build_RejectsEmptyPersona()
    {
        Assert.Throws<ArgumentException>(() => SystemPromptBuilder.Build(" "));
    }

    [Fact]
    public void Build_AlwaysIncludesSellSurplusBeforeBorrowingHint()
    {
        // Прямой запрос пользователя 2026-08-20, по следам _2bot_gpt_oss_20b_2stage_v2 (оба бота 45
        // ходов подряд брали заём вместо того, чтобы продать излишек) — эта подсказка не завязана на
        // число секторов, в отличие от CROSS-SECTOR TRADE, должна быть в промпте всегда.
        var prompt = SystemPromptBuilder.Build("persona");

        Assert.Contains("SELL THE SURPLUS, DON'T BORROW TO COVER IT", prompt);
        Assert.Contains("LOAN COST RIGHT NOW", prompt);
    }

    [Fact]
    public void Build_SingleSectorByDefault_OmitsCrossSectorTradeHint()
    {
        // Стадия 1: торговать физически не с кем, промпт не должен упоминать доску заявок как
        // приоритет над sellToSystem — это сбивало бы с толку без причины.
        var prompt = SystemPromptBuilder.Build("persona");

        Assert.DoesNotContain("CROSS-SECTOR TRADE", prompt);
    }

    [Fact]
    public void Build_MultipleSectors_AddsCrossSectorTradeHintDemotingSellToSystem()
    {
        // Прямой запрос пользователя 2026-08-20, по следам первого прогона стадии 2
        // (_2bot_gpt_oss_20b_2stage_v1): обе доски заявок технически сработали, но ни одна сделка не
        // случилась — боты использовали postSellOffer как ещё один sellToSystem, не целевой инструмент.
        var prompt = SystemPromptBuilder.Build("persona", hasMultipleSectors: true);

        Assert.Contains("CROSS-SECTOR TRADE", prompt);
        Assert.Contains("LAST resort", prompt);
        Assert.Contains("CROSS-SECTOR DEMAND", prompt);
    }

    [Fact]
    public void Build_MentionsOneCallPerTurnAndTheActualActionCap()
    {
        // Запрос пользователя 2026-08-16: "только раз за ход обращаться к LLM, и чтобы он сразу
        // формировал массив команд на ход" — промпт должен называть реальный потолок числом, не
        // абстрактным "hard limit", и явно описывать batch-формат ответа.
        var prompt = SystemPromptBuilder.Build("persona", maxActionsPerTurn: 7);

        Assert.Contains("ONE CALL DECIDES THE WHOLE TURN", prompt);
        Assert.Contains("actions", prompt);
        Assert.Contains("Put at most 7", prompt);
    }
}
