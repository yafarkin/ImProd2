namespace Game.Domain.Tests;

public class ContractConfirmationCodeTests
{
    [Fact]
    public void Generate_Returns_A_Six_Character_Code_From_The_Expected_Alphabet()
    {
        var code = ContractConfirmationCode.Generate(new Random(1));

        Assert.Equal(6, code.Length);
        Assert.All(code, c => Assert.Contains(c, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"));
    }

    [Fact]
    public void Generate_Is_Deterministic_For_The_Same_Seed()
    {
        Assert.Equal(ContractConfirmationCode.Generate(new Random(42)), ContractConfirmationCode.Generate(new Random(42)));
    }

    [Fact]
    public void Generate_Differs_Across_A_Continuing_Random_Sequence()
    {
        var random = new Random(1);

        var first = ContractConfirmationCode.Generate(random);
        var second = ContractConfirmationCode.Generate(random);

        Assert.NotEqual(first, second);
    }
}
