using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

namespace Game.Web;

/// <summary>
/// Владеет одной живой <see cref="GameSession"/> на процесс (Блок 8.1) — заготовка на будущее
/// полноценное управление несколькими сессиями (SPEC §10, Блок 10.2), которого пока нет. Открывает
/// durable-журнал при старте приложения: если журнал уже есть (перезапуск процесса) — восстанавливает
/// состояние; если нет — заводит сессию с небольшим фиксированным составом из двух команд и
/// регистрирует участников (Manager/Negotiator на каждую команду, плюс Operator и Facilitator),
/// логируя коды входа. Настоящая регистрация команд и генерация кодов ведущим/администратором —
/// Блок 9.8; здесь — временная замена, достаточная, чтобы вход по коду можно было проверить.
/// </summary>
public sealed class GameSessionHost
{
    /// <summary>Единственная живая сессия процесса.</summary>
    public GameSession Session { get; }

    /// <summary>
    /// Лок на запись/чтение <see cref="Session"/> (Блок 8.2) — <see cref="EventLog{TState}"/> и
    /// <see cref="Game.Persistence.DurableEventLog{TState}"/> сами не синхронизированы, а с этого
    /// блока в сессию пишет не только один поток посева при старте, но и фоновый
    /// <c>PhaseTimerBackgroundService</c> параллельно с чтением из потоков Blazor-circuit. Любой код
    /// в <c>Game.Web</c>, читающий или пишущий в <see cref="Session"/> после старта, обязан брать
    /// этот лок первым.
    /// </summary>
    public object SyncRoot { get; } = new();

    /// <summary>Посеянные коды входа (для отладочной страницы <c>/dev/codes</c> — Блок 9.8 её заменит).</summary>
    public IReadOnlyList<ParticipantRegistration> SeedCodes { get; }

    public GameSessionHost(ILogger<GameSessionHost> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var configPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");
        var config = GameConfigLoader.LoadFromFile(configPath);

        var sessionDirectory = Path.Combine(AppContext.BaseDirectory, "App_Data", "session");
        Directory.CreateDirectory(sessionDirectory);
        var journalPath = Path.Combine(sessionDirectory, "journal.jsonl");
        var snapshotPath = Path.Combine(sessionDirectory, "snapshot.json");

        var durableLog = DurableEventLog<GameSessionState>.Open(journalPath, snapshotPath, () => new GameSessionState(config));

        if (durableLog.Entries.Count == 0)
        {
            Session = SeedNewSession(durableLog, config);
            SeedCodes = Session.State.Participants.Values.ToList();
        }
        else
        {
            Session = new GameSession(durableLog);
            SeedCodes = Session.State.Participants.Values.ToList();
        }

        foreach (var registration in SeedCodes)
        {
            logger.LogInformation(
                "Код входа {Code}: {Role} {DisplayName}", registration.Code, registration.Role, registration.DisplayName);
        }
    }

    private static GameSession SeedNewSession(DurableEventLog<GameSessionState> durableLog, ResolvedGameConfig config)
    {
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");
        var alphaId = Ulid.NewUlid();
        var betaId = Ulid.NewUlid();

        var teams = new[]
        {
            new TeamSpec { Id = alphaId, Name = "Альфа", SectorId = sectorA.Id, StartingLoanAmount = 10_000m },
            new TeamSpec { Id = betaId, Name = "Бета", SectorId = sectorB.Id, StartingLoanAmount = 10_000m },
        };

        var preset = config.Raw.SessionPresets.Single(p => p.Id == "short");
        var endTurn = SessionEndTurnDraw.Draw(preset, Random.Shared);
        var session = GameSession.StartWithEndTurn(durableLog, preset.Id, endTurn, teams);

        session.RegisterParticipant(ParticipantRole.Manager, alphaId, "Управляющий Альфа", Random.Shared);
        session.RegisterParticipant(ParticipantRole.Negotiator, alphaId, "Переговорщик Альфа", Random.Shared);
        session.RegisterParticipant(ParticipantRole.Manager, betaId, "Управляющий Бета", Random.Shared);
        session.RegisterParticipant(ParticipantRole.Negotiator, betaId, "Переговорщик Бета", Random.Shared);
        session.RegisterParticipant(ParticipantRole.Operator, null, "Оператор", Random.Shared);
        session.RegisterParticipant(ParticipantRole.Facilitator, null, "Ведущий", Random.Shared);

        return session;
    }
}
