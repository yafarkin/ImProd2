using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Автораспределение команды в наименее заполненный сектор (Блок 9.8, SPEC §9.6).</summary>
public class SectorAssignmentCalculatorTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Sector SectorB = new("B", "Нефтегазохимия");
    private static readonly Sector SectorC = new("C", "Лес/агротекстиль");

    private static TeamSpec TeamIn(string sectorId) =>
        new() { Id = Ulid.NewUlid(), Name = "Команда", SectorId = sectorId };

    [Fact]
    public void LeastFilled_Returns_The_First_Sector_When_No_Teams_Exist_Yet()
    {
        var sector = SectorAssignmentCalculator.LeastFilled(new[] { SectorA, SectorB, SectorC }, Array.Empty<TeamSpec>());

        Assert.Equal(SectorA, sector);
    }

    [Fact]
    public void LeastFilled_Breaks_Ties_By_Config_Order()
    {
        var teams = new[] { TeamIn(SectorA.Id), TeamIn(SectorB.Id) };

        var sector = SectorAssignmentCalculator.LeastFilled(new[] { SectorA, SectorB, SectorC }, teams);

        Assert.Equal(SectorC, sector);
    }

    [Fact]
    public void LeastFilled_Picks_The_Sector_With_Fewer_Teams()
    {
        var teams = new[] { TeamIn(SectorA.Id), TeamIn(SectorA.Id), TeamIn(SectorB.Id) };

        var sector = SectorAssignmentCalculator.LeastFilled(new[] { SectorA, SectorB }, teams);

        Assert.Equal(SectorB, sector);
    }

    [Fact]
    public void LeastFilled_Throws_For_An_Empty_Sector_List()
    {
        Assert.Throws<ArgumentException>(() => SectorAssignmentCalculator.LeastFilled(Array.Empty<Sector>(), Array.Empty<TeamSpec>()));
    }
}
