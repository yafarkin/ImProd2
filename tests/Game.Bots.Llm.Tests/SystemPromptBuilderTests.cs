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
}
