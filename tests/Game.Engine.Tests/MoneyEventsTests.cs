using Game.Domain;

namespace Game.Engine.Tests;

public class MoneyEventsTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");

    private static Team NewTeam() => new(Ulid.NewUlid(), "Команда А1", SectorA);

    [Fact]
    public void LoanTaken_Increases_Balance_And_Debt_By_The_Same_Amount()
    {
        var team = NewTeam();
        var log = new EventLog<Team>(team);

        log.Append(new LoanTaken { Id = Ulid.NewUlid(), Amount = 500m });

        Assert.Equal(500m, team.Balance);
        Assert.Equal(500m, team.Debt);
    }

    [Fact]
    public void LoanInterestCharged_Debits_Balance_Only_Debt_Is_Unaffected()
    {
        var team = NewTeam();
        team.TakeLoan(1000m);
        var log = new EventLog<Team>(team);

        log.Append(new LoanInterestCharged { Id = Ulid.NewUlid(), Amount = 150m, Rate = 0.15m });

        Assert.Equal(850m, team.Balance);
        Assert.Equal(1000m, team.Debt);
    }

    [Fact]
    public void SalariesPaid_Debits_The_Balance()
    {
        var team = NewTeam();
        team.Credit(1000m);
        var log = new EventLog<Team>(team);

        log.Append(new SalariesPaid { Id = Ulid.NewUlid(), TotalWorkers = 7, Amount = 35m });

        Assert.Equal(965m, team.Balance);
    }

    [Fact]
    public void ForcedLoanTaken_Brings_Balance_To_Zero_Increases_Debt_And_Sets_The_New_Surcharge()
    {
        var team = NewTeam();
        team.Debit(80m); // баланс -80 (напр. после процентов/зарплат за ход)
        var log = new EventLog<Team>(team);

        log.Append(new ForcedLoanTaken { Id = Ulid.NewUlid(), Amount = 80m, NewPenaltyRateSurcharge = 0.1m });

        Assert.Equal(0m, team.Balance);
        Assert.Equal(80m, team.Debt);
        Assert.Equal(0.1m, team.PenaltyRateSurcharge);
    }

    [Fact]
    public void ForcedLoanTaken_Increases_An_Already_Nonzero_Surcharge_By_The_Difference_Not_By_Its_Full_New_Value()
    {
        var team = NewTeam();
        team.IncreasePenaltyRateSurcharge(0.1m); // от предыдущего принудительного займа
        team.Debit(50m);
        var log = new EventLog<Team>(team);

        log.Append(new ForcedLoanTaken { Id = Ulid.NewUlid(), Amount = 50m, NewPenaltyRateSurcharge = 0.2m });

        Assert.Equal(0.2m, team.PenaltyRateSurcharge); // не 0.1 + 0.2 = 0.3
    }
}
