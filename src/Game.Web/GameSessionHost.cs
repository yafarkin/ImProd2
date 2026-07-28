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
/// 9.8, <c>/admin</c>, SPEC §9.6) и не вызовет <see cref="StartSessionFromDraft"/>. Вход по коду
/// администратора (<see cref="AdminCode"/>) не зависит от того, стартовала ли сессия, — это
/// постоянная личность администратора на весь процесс, а не запись в журнале участников.
/// <see cref="ResetSession"/> (Блок 10.2, SPEC §10) позволяет один раз начать сессию заново поверх
/// того же состава команд и участников — для перехода от тренировочной игры к основной;
/// полноценного менеджера произвольного числа сессий с историей это по-прежнему не даёт, только то,
/// что просит SPEC §10.
/// </summary>
public sealed class GameSessionHost
{
    private readonly ILogger<GameSessionHost> _logger;
    private readonly string _sessionDirectory;
    private readonly object _draftLock = new();
    private readonly List<StagedTeamSpec> _stagedTeams = new();
    private readonly List<StagedParticipantSpec> _stagedParticipants = new();
    private ResolvedGameConfig _draftConfig = null!;

    /// <summary>Живая сессия процесса; <see langword="null"/>, пока администратор её не начал.</summary>
    public GameSession? Session { get; private set; }

    /// <summary>
    /// Конфиг, выбранный (загрузка своего файла или тренировочный) для следующего старта сессии, но
    /// ещё не подтверждённый — как и <see cref="StagedTeams"/>, хранится на уровне хоста, а не как
    /// локальное состояние Blazor-компонента, чтобы `/admin` и `/admin/teams` видели один и тот же
    /// черновик. По умолчанию — <see cref="DefaultConfig"/>.
    /// </summary>
    public ResolvedGameConfig DraftConfig
    {
        get
        {
            lock (_draftLock)
            {
                return _draftConfig;
            }
        }
    }

    /// <summary>
    /// Меняет черновой конфиг перед стартом сессии. Заодно чистит <see cref="StagedTeams"/> и
    /// привязанных к командам застейдженных участников (<see cref="StagedParticipants"/> с непустым
    /// <c>TeamId</c>, то есть управляющих) — секторы нового конфига могут не совпадать со старыми,
    /// ранее назначенные сектора команд потеряли бы смысл. Роли без команды (админ/оператор/ведущий)
    /// от смены конфига не зависят и сохраняются.
    /// </summary>
    public void SetDraftConfig(ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_draftLock)
        {
            _draftConfig = config;
            _stagedTeams.Clear();
            _stagedParticipants.RemoveAll(p => p.TeamId is not null);
        }
    }

    /// <summary>
    /// Черновик состава команд до старта сессии (Блок 9.8, экран настройки) — хранится на уровне
    /// хоста, а не как локальное состояние Blazor-компонента: компонент пересоздаётся при обновлении
    /// страницы (F5) или переходе между `/admin` и `/admin/teams`, а хост — синглтон на весь процесс.
    /// </summary>
    public IReadOnlyList<StagedTeamSpec> StagedTeams
    {
        get
        {
            lock (_draftLock)
            {
                return _stagedTeams.ToList();
            }
        }
    }

    /// <summary>Добавляет команду в черновик до старта сессии.</summary>
    public void AddStagedTeam(string name, string sectorId)
    {
        lock (_draftLock)
        {
            _stagedTeams.Add(new StagedTeamSpec(Ulid.NewUlid(), name, sectorId));
        }
    }

    /// <summary>
    /// Убирает команду из черновика до старта сессии — заодно убирает застейдженного управляющего
    /// этой команды (<see cref="StagedParticipants"/>), если он уже был назначен: без своей команды
    /// его регистрация не имеет смысла (<see cref="ParticipantRegistration"/> требует существующую
    /// команду для ролей, привязанных к ней).
    /// </summary>
    public void RemoveStagedTeam(Ulid id)
    {
        lock (_draftLock)
        {
            _stagedTeams.RemoveAll(t => t.Id == id);
            _stagedParticipants.RemoveAll(p => p.TeamId == id);
        }
    }

    /// <summary>Очищает черновик команд — при смене конфига (секторы могли поменяться) или после успешного старта сессии.</summary>
    public void ClearStagedTeams()
    {
        lock (_draftLock)
        {
            _stagedTeams.Clear();
        }
    }

    /// <summary>
    /// Черновик участников без активной сессии (Блок 9.8) — управляющих команд и ролей без команды
    /// (админ/оператор/ведущий), заведённых до старта. Каждому сразу присвоен код входа
    /// (<see cref="ShortCode"/>), который переживёт старт сессии без изменений —
    /// см. <see cref="StartSessionFromDraft"/>.
    /// </summary>
    public IReadOnlyList<StagedParticipantSpec> StagedParticipants
    {
        get
        {
            lock (_draftLock)
            {
                return _stagedParticipants.ToList();
            }
        }
    }

    /// <summary>
    /// Добавляет участника в черновик до старта сессии и сразу выдаёт ему код входа — сочетание
    /// роли/команды/имени валидирует сам <see cref="ParticipantRegistration"/> (тот же конструктор,
    /// что использует и живая сессия), лишний раз не дублируем проверку.
    /// </summary>
    public StagedParticipantSpec AddStagedParticipant(ParticipantRole role, Ulid? teamId, string displayName)
    {
        lock (_draftLock)
        {
            string code;
            do
            {
                code = ShortCode.Generate(Random.Shared);
            }
            while (_stagedParticipants.Any(p => p.Code == code) || code == AdminCode);

            _ = new ParticipantRegistration(code, role, teamId, displayName);

            var spec = new StagedParticipantSpec(Ulid.NewUlid(), code, role, teamId, displayName);
            _stagedParticipants.Add(spec);
            return spec;
        }
    }

    /// <summary>Убирает участника из черновика до старта сессии.</summary>
    public void RemoveStagedParticipant(Ulid id)
    {
        lock (_draftLock)
        {
            _stagedParticipants.RemoveAll(p => p.Id == id);
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
    /// Постоянный код входа администратора (см. doc-comment класса) — не запись в
    /// <see cref="GameSessionState.Participants"/>, а отдельная, независимая от <see cref="Session"/>
    /// личность: действителен одинаково и до, и после старта сессии, весь процесс. Генерируется на
    /// каждом старте процесса заново (не персистится на диск — живёт только в памяти, как и раньше)
    /// и перегенерируется в <see cref="HardReset"/> — единственное действие, которое действительно
    /// должно сделать администратора «другим» (полный сброс в начальное состояние).
    /// </summary>
    public string? AdminCode { get; private set; }

    public GameSessionHost(ILogger<GameSessionHost> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        var defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");
        DefaultConfig = GameConfigLoader.LoadFromFile(defaultConfigPath);

        var trainingConfigPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.training.json");
        TrainingConfig = GameConfigLoader.LoadFromFile(trainingConfigPath);

        _draftConfig = DefaultConfig;

        _sessionDirectory = Path.Combine(AppContext.BaseDirectory, "App_Data", "session");
        Directory.CreateDirectory(_sessionDirectory);

        AdminCode = ShortCode.Generate(Random.Shared);
        logger.LogInformation("Код администратора (постоянный, не меняется до полного сброса): {Code}", AdminCode);

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
    }

    /// <summary>
    /// Стартует единственную сессию процесса поверх собранного администратором состава команд
    /// (Блок 9.8, SPEC §9.6) и сразу ставит её на паузу (<see cref="GameSession.Pause"/> уже не
    /// зависит от фазы) — это даёт администратору неограниченное время на регистрацию участников
    /// (см. <see cref="RegisterParticipant"/>), не расходуя игровое время; запускает игру
    /// последующий явный <see cref="GameSession.Resume"/> со страницы администратора. Требует хотя
    /// бы одну команду — это правило именно этого, прикладного слоя (сценарий «реально начать
    /// игру»), а не движка: <see cref="GameSession.StartWithEndTurn"/> намеренно принимает и пустой
    /// список команд — им пользуются юнит-тесты движка, которым команды для проверяемой механики
    /// не нужны.
    /// </summary>
    public GameSession StartNewSession(ResolvedGameConfig config, SessionPresetConfig preset, IReadOnlyList<TeamSpec> teams)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(teams);
        if (teams.Count == 0)
        {
            throw new ArgumentException("Cannot start a session without at least one team.", nameof(teams));
        }

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
    /// Стартует сессию поверх собранного на экране настройки черновика — команд
    /// (<see cref="StagedTeams"/>) и участников (<see cref="StagedParticipants"/>: управляющие
    /// команд и роли без команды, каждый уже с выданным до старта кодом входа). Тонкая обёртка над
    /// <see cref="StartNewSession"/>, которая донабирает участников их уже готовыми кодами через
    /// <see cref="GameSession.ReregisterParticipant"/> — тот же приём, что и <see cref="ResetSession"/>
    /// использует для сохранения кодов, — чтобы код, уже показанный по QR или на бумаге до старта,
    /// остался рабочим и после него. Черновик (команды, участники, конфиг) очищается по завершении.
    /// Требует управляющего у каждой команды — без него команда не сможет ни во что играть: только у
    /// него есть право заводить (самообслуживанием) остальной свой состав и подтверждать сделки. Это
    /// проверка именно здесь, а не в <see cref="StartNewSession"/>: тот принимает только
    /// <see cref="TeamSpec"/> без какой-либо информации об участниках.
    /// </summary>
    public GameSession StartSessionFromDraft(SessionPresetConfig preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        lock (SyncRoot)
        {
            ResolvedGameConfig config;
            List<TeamSpec> teamSpecs;
            List<StagedParticipantSpec> participants;
            lock (_draftLock)
            {
                config = _draftConfig;
                teamSpecs = _stagedTeams
                    .Select(t => new TeamSpec { Id = t.Id, Name = t.Name, SectorId = t.SectorId })
                    .ToList();
                participants = _stagedParticipants.ToList();
            }

            var teamsWithoutManager = teamSpecs
                .Where(t => !participants.Any(p => p.Role == ParticipantRole.Manager && p.TeamId == t.Id))
                .Select(t => t.Name)
                .ToList();
            if (teamsWithoutManager.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot start a session while these teams have no manager assigned: {string.Join(", ", teamsWithoutManager)}.");
            }

            var session = StartNewSession(config, preset, teamSpecs);

            foreach (var participant in participants)
            {
                session.ReregisterParticipant(participant.Code, participant.Role, participant.TeamId, participant.DisplayName);
                _logger.LogInformation(
                    "Код входа {Code}: {Role} {DisplayName}", participant.Code, participant.Role, participant.DisplayName);
            }

            lock (_draftLock)
            {
                _stagedTeams.Clear();
                _stagedParticipants.Clear();
                _draftConfig = DefaultConfig;
            }

            return session;
        }
    }

    /// <summary>
    /// Начинает сессию заново поверх <see cref="DefaultConfig"/>, сохраняя тот же состав команд и
    /// те же коды входа участников (Блок 10.2, SPEC §10: «те же команды и логины, но полностью
    /// независимое состояние и репутация») — переход от тренировочной игры к основной. Баланс и
    /// долг каждой команды обнуляются вместе со всем остальным состоянием — «полностью независимое
    /// состояние» и так буквально означает старт с нуля; первый кредит команда, как и в самом
    /// начале, берёт заново сама. Как и <see cref="StartNewSession"/>, сразу ставит новую сессию
    /// на паузу.
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
                .Select(team => new TeamSpec { Id = team.Id, Name = team.Name, SectorId = team.Sector.Id })
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
    /// <see cref="ResetSession"/>, на случай если историю забыли выгрузить. Черновик команд и
    /// участников (<see cref="StagedTeams"/>, <see cref="StagedParticipants"/>) и черновой конфиг
    /// (<see cref="DraftConfig"/>) очищаются всегда, независимо от того, была ли сессия начата.
    /// <see cref="AdminCode"/> перегенерируется — единственный способ действительно сменить личность
    /// администратора (см. его doc-comment).
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

            AdminCode = ShortCode.Generate(Random.Shared);
            _logger.LogInformation("Код администратора (постоянный, не меняется до полного сброса): {Code}", AdminCode);
        }

        lock (_draftLock)
        {
            _draftConfig = DefaultConfig;
            _stagedTeams.Clear();
            _stagedParticipants.Clear();
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
public sealed record StagedTeamSpec(Ulid Id, string Name, string SectorId);

/// <summary>Один участник в черновике до старта сессии — см. <see cref="GameSessionHost.StagedParticipants"/>.</summary>
public sealed record StagedParticipantSpec(Ulid Id, string Code, ParticipantRole Role, Ulid? TeamId, string DisplayName);
