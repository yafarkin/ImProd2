namespace Game.Domain.Tests;

/// <summary>Тонкая обёртка над <see cref="ShortCode"/> (форма/детерминизм проверены в <c>ShortCodeTests</c>).</summary>
public class ContractConfirmationCodeTests
{
    [Fact]
    public void Generate_Delegates_To_ShortCode()
    {
        Assert.Equal(ShortCode.Generate(new Random(7)), ContractConfirmationCode.Generate(new Random(7)));
    }
}
