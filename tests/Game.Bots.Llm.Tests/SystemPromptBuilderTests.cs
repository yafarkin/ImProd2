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
    public void Build_AlwaysIncludesSellSurplusHint()
    {
        // Изначально запрос пользователя 2026-08-20 был про займы (боты брали заём вместо продажи
        // излишка) — механика займа убрана (docs/TODO.md #23), но сама подсказка "продай, не сиди на
        // остатке" осталась актуальной и не завязана на число секторов, должна быть в промпте всегда.
        var prompt = SystemPromptBuilder.Build("persona");

        Assert.Contains("SELL THE SURPLUS, DON'T JUST LET THE BALANCE DRIFT DOWN", prompt);
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
    public void Build_MultipleSectors_AddsCrossSectorTradeHintAsABonusNotAReplacement()
    {
        // Прямой запрос пользователя 2026-08-20, по следам первого прогона стадии 2
        // (_2bot_gpt_oss_20b_2stage_v1): обе доски заявок технически сработали, но ни одна сделка не
        // случилась — боты использовали postSellOffer как ещё один sellToSystem, не целевой инструмент.
        // v3 (тот же день) показал обратную крайность прежней формулировки "LAST resort": Бот 2 почти
        // перестал вызывать sellToSystem вовсе (16 раз на ходах 1-25 → 0 раз на ходах 76-90), уйдя в
        // postSellOffer, которые почти никогда не исполнялись — 40 ходов подряд падения net worth.
        // Переформулировано: sellToSystem остаётся действием по умолчанию, доска — бонус сверху.
        var prompt = SystemPromptBuilder.Build("persona", hasMultipleSectors: true);

        Assert.Contains("CROSS-SECTOR TRADE", prompt);
        Assert.Contains("BONUS", prompt);
        Assert.Contains("not a replacement", prompt);
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
