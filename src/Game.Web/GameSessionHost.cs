using System.Text.Json;
using Game.Config.Loading;
using Game.Config.Session;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

namespace Game.Web;

/// <summary>
/// Владеет одной живой <see cref="GameSession"/> на процесс (Блок 8.1) — заготовка на будущее
/// полноценное управление несколькими сессиями (SPEC §10, Блок 10.2), которого пока нет. При
/// старте приложения проверяет, сохранён ли в <c>App_Data/session/config.json</c> конфиг ранее
/// стартовавшей сессии: если да — открывает durable-журнал и восстанавливает состояние; если нет —
/// <see cref="Session"/> остаётся <see langword="null"/> до тех пор, пока администратор не пройдёт
/// экран настройки (Блок 9.8, <c>/admin</c>, SPEC §9.6) и не вызовет <see cref="StartNewSession"/>.
/// До этого момента вход по коду администратора невозможен обычным путём (сверяться не с чем — в
/// сессии ещё нет участников), поэтому на время ожидания настройки генерируется одноразовый
/// <see cref="AdminBootstrapCode"/>, живущий вне журнала.
/// </summary>
public sealed class GameSessionHost
{
    private readonly ILogger<GameSessionHost> _logger;
    private readonly string _sessionDirectory;

    /// <summary>Живая сессия процесса; <see langword="null"/>, пока администратор её не начал.</summary>
    public GameSession? Session { get; private set; }

    /// <summary>
    /// Лок на запись/чтение <see cref="Session"/> (Блок 8.2) — <see cref="EventLog{TState}"/> и
    /// <see cref="Game.Persistence.DurableEventLog{TState}"/> сами не синхронизированы, а в сессию
    /// пишет не только страница администратора, но и фоновый <c>PhaseTimerBackgroundService</c>
    /// параллельно с чтением из потоков Blazor-circuit. Любой код в <c>Game.Web</c>, читающий или
    /// пишущий в <see cref="Session"/>, обязан брать этот лок первым.
    /// </summary>
    public object SyncRoot { get; } = new();

    /// <summary>Конфиг по умолчанию (SPEC-заглушка пилота) — предложен на экране администратора, может быть заменён загрузкой своего файла.</summary>
    public ResolvedGameConfig DefaultConfig { get; }

    /// <summary>
    /// Одноразовый код для входа на <c>/admin</c> до старта сессии (см. doc-comment класса) — не
    /// связан с <see cref="GameSessionState.Participants"/>, действителен только пока
    /// <see cref="Session"/> равен <see langword="null"/>. После старта сессии администратор
    /// получает обычный, постоянный код через <see cref="RegisterParticipant"/>.
    /// </summary>
    public string? AdminBootstrapCode { get; }

    public GameSessionHost(ILogger<GameSessionHost> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        var defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");
        DefaultConfig = GameConfigLoader.LoadFromFile(defaultConfigPath);

        _sessionDirectory = Path.Combine(AppContext.BaseDirectory, "App_Data", "session");
        Directory.CreateDirectory(_sessionDirectory);

        var configJsonPath = Path.Combine(_sessionDirectory, "config.json");
        if (File.Exists(configJsonPath))
        {
            var config = GameConfigLoader.Load(File.ReadAllText(configJsonPath));
            var durableLog = DurableEventLog<GameSessionState>.Open(
                Path.Combine(_sessionDirectory, "journal.jsonl"),
                Path.Combine(_sessionDirectory, "snapshot.json"),
                () => new GameSessionState(config));

            Session = new GameSession(durableLog);

            foreach (var registration in Session.State.Participants.Values)
            {
                logger.LogInformation(
                    "Код входа {Code}: {Role} {DisplayName}", registration.Code, registration.Role, registration.DisplayName);
            }
        }
        else
        {
            AdminBootstrapCode = ShortCode.Generate(Random.Shared);
            logger.LogInformation("Код администратора (настройка сессии, только до старта): {Code}", AdminBootstrapCode);
        }
    }

    /// <summary>
    /// Стартует единственную сессию процесса поверх собранного администратором состава команд
    /// (Блок 9.8, SPEC §9.6) и сразу ставит её на паузу (<see cref="GameSession.Pause"/> уже не
    /// зависит от фазы) — это даёт администратору неограниченное время на регистрацию участников
    /// (см. <see cref="RegisterParticipant"/>), не расходуя игровое время; запускает игру
    /// последующий явный <see cref="GameSession.Resume"/> со страницы администратора.
    /// </summary>
    public GameSession StartNewSession(ResolvedGameConfig config, SessionPresetConfig preset, IReadOnlyList<TeamSpec> teams)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(teams);

        lock (SyncRoot)
        {
            if (Session is not null)
            {
                throw new InvalidOperationException("Session has already been started.");
            }

            File.WriteAllText(Path.Combine(_sessionDirectory, "config.json"), JsonSerializer.Serialize(config.Raw));

            var durableLog = DurableEventLog<GameSessionState>.Open(
                Path.Combine(_sessionDirectory, "journal.jsonl"),
                Path.Combine(_sessionDirectory, "snapshot.json"),
                () => new GameSessionState(config));

            var endTurn = SessionEndTurnDraw.Draw(preset, Random.Shared);
            var session = GameSession.StartWithEndTurn(durableLog, preset.Id, endTurn, teams);
            session.Pause();

            Session = session;
            return session;
        }
    }

    /// <summary>
    /// Регистрирует участника уже стартовавшей сессии и логирует выданный код (Блок 9.8) — тонкая
    /// обёртка над <see cref="GameSession.RegisterParticipant"/>, чтобы страница администратора не
    /// работала с <see cref="Random"/> напрямую и логирование кодов оставалось в одном месте.
    /// </summary>
    public EventLogEntry<GameSessionState> RegisterParticipant(ParticipantRole role, Ulid? teamId, string displayName)
    {
        lock (SyncRoot)
        {
            if (Session is null)
            {
                throw new InvalidOperationException("Cannot register a participant before the session is started.");
            }

            var entry = Session.RegisterParticipant(role, teamId, displayName, Random.Shared);
            var registered = (ParticipantRegistered)entry.Change;
            _logger.LogInformation(
                "Код входа {Code}: {Role} {DisplayName}", registered.Code, registered.Role, registered.DisplayName);
            return entry;
        }
    }
}
