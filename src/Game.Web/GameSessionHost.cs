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
/// 9.8, <c>/admin</c>, SPEC §9.6) и не вызовет <see cref="StartSessionFromDraft"/>. Черновик до
/// старта сессии — тоже durable-журнал (<c>App_Data/draft</c>, <see cref="DraftState"/>), открытый
/// безусловно с самого начала процесса: конфигурирование команд и персонала до старта — такая же
/// последовательность событий, как и сама игра, и переживает перезапуск процесса точно так же. Вход
/// по коду администратора работает так же, как и у любой другой роли, — администратор ничем не
/// отличается от оператора или ведущего: та же запись в черновике/журнале участников
/// (<see cref="ParticipantRole.Administrator"/>), тот же путь входа. Единственная особенность —
/// <see cref="EnsureFirstAdministrator"/>: если во всём процессе ещё нет ни одного администратора
/// (самый первый запуск или сразу после <see cref="HardReset"/>), он заводится автоматически, чтобы
/// на `/admin` вообще можно было зайти. <see cref="AdminCode"/> — просто код этого первого
/// администратора; дополнительных можно завести вручную на «Персонал», как и любую другую роль без
/// команды.
/// <see cref="ResetSession"/> (Блок 10.2, SPEC §10) позволяет один раз начать сессию заново поверх
/// того же состава команд и участников — для перехода от тренировочной игры к основной;
/// полноценного менеджера произвольного числа сессий с историей это по-прежнему не даёт, только то,
/// что просит SPEC §10.
/// </summary>
public sealed class GameSessionHost
{
    private readonly ILogger<GameSessionHost> _logger;
    private readonly string _sessionDirectory;
    private readonly string _draftDirectory;
    private readonly object _draftLock = new();
    private IEventLog<DraftState> _draftLog = null!;
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
    /// ранее назначенные сектора команд потеряли бы смысл: каждая команда убирается тем же событием
    /// <see cref="TeamUnstaged"/>, что и ручное удаление, только пачкой. Роли без команды
    /// (админ/оператор/ведущий) от смены конфига не зависят и сохраняются. Сам конфиг — не событие
    /// (см. doc-comment <see cref="DraftState"/>): персистируется отдельным файлом
    /// <c>App_Data/draft/config.json</c>, той же парой <c>JsonSerializer.Serialize(config.Raw)</c> /
    /// <see cref="GameConfigLoader.Load"/>, что уже использует сессия для своего <c>config.json</c>.
    /// </summary>
    public void SetDraftConfig(ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_draftLock)
        {
            foreach (var teamId in _draftLog.State.Teams.Keys.ToList())
            {
                _draftLog.Append(new TeamUnstaged { Id = Ulid.NewUlid(), TeamId = teamId });
            }

            File.WriteAllText(Path.Combine(_draftDirectory, "config.json"), JsonSerializer.Serialize(config.Raw));
            _draftConfig = config;
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
                return _draftLog.State.Teams.Values.ToList();
            }
        }
    }

    /// <summary>Добавляет команду в черновик до старта сессии. См. <see cref="TeamStaged"/>.</summary>
    public void AddStagedTeam(string name, string sectorId)
    {
        lock (_draftLock)
        {
            _draftLog.Append(new TeamStaged { Id = Ulid.NewUlid(), TeamId = Ulid.NewUlid(), Name = name, SectorId = sectorId });
        }
    }

    /// <summary>
    /// Убирает команду из черновика до старта сессии — заодно убирает застейдженного управляющего
    /// этой команды (<see cref="StagedParticipants"/>), если он уже был назначен: без своей команды
    /// его регистрация не имеет смысла (<see cref="ParticipantRegistration"/> требует существующую
    /// команду для ролей, привязанных к ней). См. <see cref="TeamUnstaged"/>.
    /// </summary>
    public void RemoveStagedTeam(Ulid id)
    {
        lock (_draftLock)
        {
            _draftLog.Append(new TeamUnstaged { Id = Ulid.NewUlid(), TeamId = id });
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
                return _draftLog.State.Participants.Values.ToList();
            }
        }
    }

    /// <summary>
    /// Добавляет участника в черновик до старта сессии и сразу выдаёт ему код входа — сочетание
    /// роли/команды/имени валидирует сам <see cref="ParticipantRegistration"/> (тот же конструктор,
    /// что использует и живая сессия), лишний раз не дублируем проверку. Валидация происходит до
    /// <c>Append</c> — <see cref="ParticipantStaged"/> сам по себе не бросает. См. <see cref="ParticipantStaged"/>.
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
            while (_draftLog.State.Participants.Values.Any(p => p.Code == code));

            _ = new ParticipantRegistration(code, role, teamId, displayName);

            var participantId = Ulid.NewUlid();
            _draftLog.Append(new ParticipantStaged
            {
                Id = Ulid.NewUlid(),
                ParticipantId = participantId,
                Code = code,
                Role = role,
                TeamId = teamId,
                DisplayName = displayName,
            });

            return _draftLog.State.Participants[participantId];
        }
    }

    /// <summary>Убирает участника из черновика до старта сессии. См. <see cref="ParticipantUnstaged"/>.</summary>
    public void RemoveStagedParticipant(Ulid id)
    {
        lock (_draftLock)
        {
            _draftLog.Append(new ParticipantUnstaged { Id = Ulid.NewUlid(), ParticipantId = id });
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

    /// <summary>Тренировочный конфиг (Блок 10.2, SPEC §10) — те же секторы/материалы, что и <see cref="DefaultConfig"/>, но короткий пресет (8–10 ходов), суммарно ~50–60 минут на сессию.</summary>
    public ResolvedGameConfig TrainingConfig { get; }

    /// <summary>
    /// Отладочный конфиг — те же секторы/материалы, что и <see cref="DefaultConfig"/>, но очень
    /// короткий ход (30 секунд суммарно на фазы) и длинная сессия (300 ходов), чтобы наблюдать в
    /// динамике, как меняются цифры и графики, не дожидаясь реальной игры.
    /// </summary>
    public ResolvedGameConfig DebugConfig { get; }

    /// <summary>
    /// Код входа первого администратора — обычная запись с ролью
    /// <see cref="ParticipantRole.Administrator"/>, как у любого другого участника: до старта сессии
    /// ищется среди <see cref="StagedParticipants"/>, после — среди
    /// <see cref="GameSessionState.Participants"/>. Заводится автоматически, если такой роли ещё нет
    /// нигде, — см. <see cref="EnsureFirstAdministrator"/>. Если администраторов несколько (можно
    /// завести ещё вручную на «Персонал»), возвращает того, кто был заведён первым.
    /// </summary>
    public string? AdminCode =>
        Session is not null
            ? Session.State.Participants.Values.FirstOrDefault(p => p.Role == ParticipantRole.Administrator)?.Code
            : StagedParticipants.FirstOrDefault(p => p.Role == ParticipantRole.Administrator)?.Code;

    public GameSessionHost(ILogger<GameSessionHost> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        var defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");
        DefaultConfig = GameConfigLoader.LoadFromFile(defaultConfigPath);

        var trainingConfigPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.training.json");
        TrainingConfig = GameConfigLoader.LoadFromFile(trainingConfigPath);

        var debugConfigPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.debug.json");
        DebugConfig = GameConfigLoader.LoadFromFile(debugConfigPath);

        _sessionDirectory = Path.Combine(AppContext.BaseDirectory, "App_Data", "session");
        Directory.CreateDirectory(_sessionDirectory);

        _draftDirectory = Path.Combine(AppContext.BaseDirectory, "App_Data", "draft");
        Directory.CreateDirectory(_draftDirectory);

        var draftConfigPath = Path.Combine(_draftDirectory, "config.json");
        _draftConfig = File.Exists(draftConfigPath) ? GameConfigLoader.Load(File.ReadAllText(draftConfigPath)) : DefaultConfig;
        _draftLog = OpenDraftLog();

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
                    "Код {Role} {DisplayName}: {Code}", registration.Role, registration.DisplayName, registration.Code);
            }
        }

        EnsureFirstAdministrator();
        logger.LogInformation("Код администратора: {Code}", AdminCode);
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
                "Код {Role} {DisplayName}: {Code}", registered.Role, registered.DisplayName, registered.Code);
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
                teamSpecs = _draftLog.State.Teams.Values
                    .Select(t => new TeamSpec { Id = t.Id, Name = t.Name, SectorId = t.SectorId })
                    .ToList();
                participants = _draftLog.State.Participants.Values.ToList();
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
                    "Код {Role} {DisplayName}: {Code}", participant.Role, participant.DisplayName, participant.Code);
            }

            lock (_draftLock)
            {
                ArchiveDraftFiles();
                _draftLog = OpenDraftLog();
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
                    "Код {Role} {DisplayName}: {Code} (сохранён после сброса)",
                    participant.Role, participant.DisplayName, participant.Code);
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
    /// Администратор при этом временно пропадает — <see cref="EnsureFirstAdministrator"/> в конце
    /// заводит нового, с новым кодом: это и есть единственный способ действительно сменить личность
    /// администратора (старый код, будучи архивированной записью в архивированном журнале, больше
    /// нигде не встречается и перестаёт работать).
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
        }

        lock (_draftLock)
        {
            ArchiveDraftFiles();
            _draftLog = OpenDraftLog();
            _draftConfig = DefaultConfig;
        }

        EnsureFirstAdministrator();
        _logger.LogInformation("Код администратора: {Code}", AdminCode);
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

    /// <summary>Переименовывает файлы предыдущего черновика с меткой времени вместо удаления — та же страховка, что и <see cref="ArchiveSessionFiles"/>.</summary>
    private void ArchiveDraftFiles()
    {
        var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        foreach (var name in new[] { "config.json", "journal.jsonl", "snapshot.json" })
        {
            var path = Path.Combine(_draftDirectory, name);
            if (File.Exists(path))
            {
                File.Move(path, Path.Combine(_draftDirectory, $"{name}.{suffix}.bak"));
            }
        }
    }

    /// <summary>
    /// Заводит первого администратора, если во всём процессе ещё ни одного нет, — обычным путём,
    /// как и любого другого участника: <see cref="RegisterParticipant"/>, если сессия уже идёт, иначе
    /// <see cref="AddStagedParticipant"/>. Вызывается из конструктора (самый первый запуск процесса
    /// или восстановление после рестарта) и из <see cref="HardReset"/> (после него администратор
    /// пропадает вместе со всем остальным) — иначе `/admin` был бы недоступен вообще никому. Оба
    /// вызывающих места сами логируют итоговый <see cref="AdminCode"/> после этого вызова — не только
    /// когда он только что создан, но и на каждом обычном перезапуске поверх уже существующего, чтобы
    /// код всегда можно было найти в логе процесса, а не только в момент первого создания.
    /// </summary>
    private void EnsureFirstAdministrator()
    {
        if (Session is not null)
        {
            if (!Session.State.Participants.Values.Any(p => p.Role == ParticipantRole.Administrator))
            {
                RegisterParticipant(ParticipantRole.Administrator, null, "Администратор");
            }
        }
        else if (!StagedParticipants.Any(p => p.Role == ParticipantRole.Administrator))
        {
            AddStagedParticipant(ParticipantRole.Administrator, null, "Администратор");
        }
    }

    /// <summary>
    /// Открывает durable-журнал черновика (Блок 9.8) по путям в <see cref="_draftDirectory"/> — если
    /// файлов ещё нет (первый запуск процесса или сразу после <see cref="ArchiveDraftFiles"/>),
    /// начинает с чистого <see cref="DraftState"/>. Выбранный конфиг сюда не входит — см.
    /// <see cref="_draftConfig"/> и doc-comment <see cref="DraftState"/>.
    /// </summary>
    private IEventLog<DraftState> OpenDraftLog()
    {
        return DurableEventLog<DraftState>.Open(
            Path.Combine(_draftDirectory, "journal.jsonl"),
            Path.Combine(_draftDirectory, "snapshot.json"),
            () => new DraftState());
    }
}
