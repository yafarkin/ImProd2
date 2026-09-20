using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Прогон полной партии простыми ботами (Блок 7.1, BUILD_PLAN «Фаза 7»): готово, когда партия ботов
/// проходит полную сессию без вмешательства извне — на том конфиге, во что реально играют.
/// </summary>
public class BotSessionRunnerTests
{
    /// <summary>
    /// Полная партия на <b>поставочной</b> паре конфигов (боевая цепочка + сессионный файл), все 98
    /// ходов, по 2 команды на каждый из трёх секторов. Тест был выключен с 2026-08-21
    /// (<c>docs/TODO.md</c> №26: комбинированный legacy-конфиг потерял прибыльность при переходе на
    /// себестоимость) и переведён сюда 2026-09-08: перекалибровывать legacy-файл незачем — поставка
    /// это две производственные модели и один сессионный файл, и проверять экономику надо на них.
    ///
    /// <para><b>Партия прогоняется до конца, а не 15 ходов.</b> Короткий прогон про экономику ничего
    /// не говорит: цепочка по построению уходит в глубокий минус на стройке и обязана выйти в плюс
    /// к 75-му ходу из 98 (правило окупаемости, <c>docs/levers.md</c>). На 15-м ходу все команды
    /// закономерно в минусе на 17–25 тысяч, и любой порог там был бы взят с потолка. Полные 98 ходов
    /// шестью ботами считаются около секунды — платить за осмысленность нечем.</para>
    /// </summary>
    [Fact]
    public void Bots_Complete_A_Full_Session_On_The_Shipped_Config_And_Finish_In_The_Black()
    {
        var config = ShippedBotSession.LoadConfig();
        var (session, bots) = ShippedBotSession.StartSession(config, config.Raw.Duration.MaxTurns);

        BotSessionRunner.RunToCompletion(session, bots, new Random(1));

        Assert.True(session.State.IsFinished);
        Assert.True(session.VerifyIntegrity());

        var changes = session.Entries.Select(e => e.Change).ToList();
        Assert.Contains(changes, c => c is MaterialSoldToSystem); // продают системе
        Assert.Contains(changes, c => c is FactoryBuilt); // строят добычу и весь передел за ней
        Assert.Contains(changes, c => c is ContractDelivered); // и торгуют друг с другом
        Assert.DoesNotContain(changes, c => c is DeliveryMissed); // боты всегда успевают накопить объём к поставке

        // Каждая команда заканчивает партию в плюсе — то самое, ради чего калибруется цепочка
        // (docs/levers.md: выйти в плюс не позже 75-го хода при достижимой, не идеальной игре).
        foreach (var bot in bots)
        {
            var balance = session.State.Teams[bot.TeamId].Balance;
            Assert.True(balance > 0m, $"команда {bot.TeamId} закончила партию с балансом {balance:F0}");
        }
    }

    [Fact]
    public void BuildFactory_Throws_When_The_Factory_Definition_Belongs_To_Another_Sector()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, endTurn: 15,
            new[] { new TeamSpec { Id = teamId, Name = "Команда А", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        Assert.Throws<ArgumentException>(() => session.BuildFactory(teamId, "oil-well"));
    }
}
