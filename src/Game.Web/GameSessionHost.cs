using System.Text.Json;
using Game.Config.Loading;
using Game.Config.Session;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

namespace Game.Web;

/// <summary>
/// Владеет одной живой <see cref="GameSession"/> на процесс (Блок 8.1). При старте приложения
/// проверяет, сохранён ли в <c>App_Data/session/config.json</c> конфиг ранее стартовавшей сессии:
/// если да — открывает durable-журнал и восстанавливает состояние; если нет — <see cref="Session"/>
/// остаётся <see langword="null"/> до тех пор, пока администратор не пройдёт экран настройки (Блок
/// 9.8, <c>/admin</c>, SPEC §9.6) и не вызовет <see cref="StartNewSession"/>. До этого момента вход
/// по коду администратора невозможен обычным путём (сверяться не с чем — в сессии ещё нет
/// участников), поэтому на время ожидания настройки генерируется одноразовый
/// <see cref="AdminBootstrapCode"/>, живущий вне журнала. <see cref="ResetSession"/> (Блок 10.2,
/// SPEC §10) позволяет один раз начать сессию заново поверх того же состава команд и участников —
/// для перехода от тренировочной игры к основной; полноценного менеджера произвольного числа
/// сессий с историей это по-прежнему не даёт, только то, что просит SPEC §10.
/// </summary>
public sealed class GameSessionHost
{
    private readonly ILogger<GameSessionHost> _logger;
    private readonly string _sessionDirectory;
    private readonly object _stagedTeamsLock = new();
    private readonly List<StagedTeamSpec> _stagedTeams = new();

    /// <summary>Живая сессия процесса; <see langword="null"/>, пока администратор её не начал.</summary>
    public GameSession? Session { get; private set; }

    /// <summary>
    /// Черновик состава команд до старта сессии (Блок 9.8, экран настройки) — хранится на уровне
    /// хоста, а не как локальное состояние Blazor-компонента: компонент пересоздаётся при обновлении
    /// страницы (F5) или переходе между `/admin` и `/admin/teams`, а хост — синглтон на весь процесс.
    /// </summary>
    public IReadOnlyList<StagedTeamSpec> StagedTeams
    {
        get
        {
            lock (_stagedTeamsLock)
            {
                return _stagedTeams.ToList();
            }
        }
    }

    /// <summary>Добавляет команду в черновик до старта сессии.</summary>
    public void AddStagedTeam(string name, string sectorId, decimal startingLoanAmount)
    {
        lock (_stagedTeamsLock)
        {
            _stagedTeams.Add(new StagedTeamSpec(Ulid.NewUlid(), name, sectorId, startingLoanAmount));
        }
    }

    /// <summary>Убирает команду из черновика до старта сессии.</summary>
    public void RemoveStagedTeam(Ulid id)
    {
        lock (_stagedTeamsLock)
        {
            _stagedTeams.RemoveAll(t => t.Id == id);
        }
    }

    /// <summary>Очищает черновик — при смене конфига (секторы могли поменяться) или после успешного старта сессии.</summary>
    public void ClearStagedTeams()
    {
        lock (_stagedTeamsLock)
        {
            _stagedTeams.Clear();
        }
    }

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

    /// <summary>Тренировочный конфиг (Блок 10.2, SPEC §10) — те же секторы/материалы, что и <see cref="DefaultConfig"/>, но короткий пресет и тайминги фаз «по минуте».</summary>
    public ResolvedGameConfig TrainingConfig { get; }

    /// <summary>
    /// Одноразовый код для входа на <c>/admin</c> до старта сессии (см. doc-comment класса) — не
    /// связан с <see cref="GameSessionState.Participants"/>, действителен только пока
    /// <see cref="Session"/> равен <see langword="null"/>. После старта сессии администратор
    /// получает обычный, постоянный код через <see cref="RegisterParticipant"/>. Перегенерируется в
    /// <see cref="HardReset"/> — иначе старый код, напечатанный при первом запуске процесса,
    /// оставался бы рабочим бессрочно (валидность зависит только от <see cref="Session"/> будучи
    /// <see langword="null"/>, а не от того, использовали код уже или нет).
    /// </summary>
    public string? AdminBootstrapCode { get; private set; }

    public GameSessionHost(ILogger<GameSessionHost> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        var defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");
        DefaultConfig = GameConfigLoader.LoadFromFile(defaultConfigPath);

        var trainingConfigPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.training.json");
        TrainingConfig = GameConfigLoader.LoadFromFile(trainingConfigPath);

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

    /// <summary>
    /// Начинает сессию заново поверх <see cref="DefaultConfig"/>, сохраняя тот же состав команд и
    /// те же коды входа участников (Блок 10.2, SPEC §10: «те же команды и логины, но полностью
    /// независимое состояние и репутация») — переход от тренировочной игры к основной. Стартовый
    /// заём каждой команды обнуляется: <see cref="Domain.Team"/> не хранит исходную сумму займа
    /// отдельно от уже эволюционировавших <c>Balance</c>/<c>Debt</c>, а «полностью независимое
    /// состояние» и так буквально означает старт с нуля. Как и <see cref="StartNewSession"/>, сразу
    /// ставит новую сессию на паузу.
    /// </summary>
    public GameSession ResetSession(SessionPresetConfig preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        lock (SyncRoot)
        {
            if (Session is null)
            {
                throw new InvalidOperationException("Cannot reset before a session has been started.");
            }

            var teams = Session.State.Teams.Values
                .Select(team => new TeamSpec { Id = team.Id, Name = team.Name, SectorId = team.Sector.Id, StartingLoanAmount = 0m })
                .ToList();
            var participants = Session.State.Participants.Values.ToList();

            ArchiveSessionFiles();

            File.WriteAllText(Path.Combine(_sessionDirectory, "config.json"), JsonSerializer.Serialize(DefaultConfig.Raw));

            var durableLog = DurableEventLog<GameSessionState>.Open(
                Path.Combine(_sessionDirectory, "journal.jsonl"),
                Path.Combine(_sessionDirectory, "snapshot.json"),
                () => new GameSessionState(DefaultConfig));

            var endTurn = SessionEndTurnDraw.Draw(preset, Random.Shared);
            var session = GameSession.StartWithEndTurn(durableLog, preset.Id, endTurn, teams);
            session.Pause();

            foreach (var participant in participants)
            {
                session.ReregisterParticipant(participant.Code, participant.Role, participant.TeamId, participant.DisplayName);
                _logger.LogInformation(
                    "Код входа {Code}: {Role} {DisplayName} (сохранён после сброса)",
                    participant.Code, participant.Role, participant.DisplayName);
            }

            Session = session;
            return session;
        }
    }

    /// <summary>
    /// Полный сброс в начальное состояние процесса — не «та же сессия заново» (см.
    /// <see cref="ResetSession"/>), а вообще без активной сессии: доступен как во время игры, так и
    /// на экране настройки. Файлы архивируются, а не удаляются — та же страховка, что и у
    /// <see cref="ResetSession"/>, на случай если историю забыли выгрузить. Черновик команд
    /// (<see cref="StagedTeams"/>) очищается всегда, независимо от того, была ли сессия начата.
    /// <see cref="AdminBootstrapCode"/> не перегенерируется — старый по-прежнему действителен, пока
    /// <see cref="Session"/> снова не станет не-<see langword="null"/>.
    /// </summary>
    public void HardReset()
    {
        lock (SyncRoot)
        {
            if (Session is not null)
            {
                ArchiveSessionFiles();
                Session = null;
            }

            AdminBootstrapCode = ShortCode.Generate(Random.Shared);
            _logger.LogInformation("Код администратора (настройка сессии, только до старта): {Code}", AdminBootstrapCode);
        }

        lock (_stagedTeamsLock)
        {
            _stagedTeams.Clear();
        }
    }

    /// <summary>Переименовывает файлы предыдущей сессии с меткой времени вместо удаления — на случай, если историю (Блок 10.1) забыли выгрузить до сброса.</summary>
    private void ArchiveSessionFiles()
    {
        var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        foreach (var name in new[] { "config.json", "journal.jsonl", "snapshot.json" })
        {
            var path = Path.Combine(_sessionDirectory, name);
            if (File.Exists(path))
            {
                File.Move(path, Path.Combine(_sessionDirectory, $"{name}.{suffix}.bak"));
            }
        }
    }
}

/// <summary>Одна команда в черновике до старта сессии — см. <see cref="GameSessionHost.StagedTeams"/>.</summary>
public sealed record StagedTeamSpec(Ulid Id, string Name, string SectorId, decimal StartingLoanAmount);
