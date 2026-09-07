using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Ботовая стратегия команды, параметризованная по двум независимым осям (Блок 7.1-7.3.2,
/// BUILD_PLAN «Фаза 7», <c>docs/balancing-bots.md</c> §2) — полная вертикальная интеграция внутри
/// своего сектора — строит все типы фабрик сектора и нанимает базовую численность рабочих (SPEC
/// §5.6), продаёт остаток либо контрагенту через биржевой стакан (<see cref="ComputeSellOrders"/>/
/// <see cref="OrderBook"/>), либо системе (<see cref="SellSurplusToSystem"/>, SPEC §5.4), закупает
/// то, что не производит сам сектор, тем же стаканом (<see cref="ComputeBuyOrders"/>). Бот не ведёт
/// переговоры — сведение заявок стакана и согласование пары ботов на контракт целиком забота
/// вызывающего кода (<see cref="BotSessionRunner"/>, <see cref="OrderBook.Match"/>).
///
/// <para>
/// <c>leverage</c> (0..1, Блок 7.3.2) — аппетит к отрицательному балансу (нет ни займа, ни процента
/// как класса механики, docs/TODO.md #23 — это исключительно собственная, добровольная осторожность
/// бота, ничем в движке не наказывается): <c>0</c> — терпит минимум минуса, строит только то, на что
/// хватает уже заработанного (<see cref="BuildNewlyUnlockedFactories"/>), ничего не вкладывает сверх
/// темпа, который тянет маржа; <c>1</c> — терпит глубокий минус ради немедленной постройки/вложений,
/// вкладывает в R&amp;D (командное и по каждой фабрике) на потолок с первого хода. Промежуточные
/// значения — линейная интерполяция доли между полюсами (та же схема, что и в
/// `docs/balancing-bots.md` §2, «Промежуточные значения»).
/// </para>
/// <para>
/// <c>profile</c> (0..1, Блок 7.3.2) — распределение усилий по времени: <c>0</c> — фронт-лоадед,
/// вкладывает на полную с первого хода (значение по умолчанию — так вела себя <see cref="SimpleBot"/>
/// до Блока 7.3.2, регрессионный ориентир); <c>1</c> — бэк-лоадед, держит нулевые вложения почти всю
/// партию, резкий рывок ближе к концу. Момент переключения темпа — <see cref="UpdateInvestmentPace"/>.
/// </para>
///
/// <c>leverage≈1, profile≈0</c> (значения по умолчанию конструктора) — та самая единственная точка
/// сетки, которой был весь <see cref="SimpleBot"/> до Блока 7.3.2, не отдельный класс
/// (`docs/balancing-bots.md` §2: «SimpleBot в текущем виде станет одной из ячеек сетки»). Сама
/// жадность/агрессивность заявок стакана (<see cref="MinSellMarginRate"/>, <see
/// cref="MaxBuyPremiumRate"/> и т.д.) сознательно НЕ входит в сетку v1 — общая для всех ячеек
/// (`docs/balancing-bots.md` §2, «Осознанно не входит в сетку»).
///
/// <para>
/// <b>Финансовая осторожность</b> (запрос пользователя, найдено первым калибровочным прогоном
/// `metallurgy.json` — Блок 7.3.1-7.3.6: бот раскручивал спираль принудительных займов, продолжая
/// строить фабрики и вкладывать в R&amp;D независимо от того, что кассовый разрыв только рос).
/// <see cref="UpdateFinancialTrend"/> отслеживает тренд чистой стоимости команды и подрезает темп
/// расширения/вложений (не номинальные <c>leverage</c>/<c>profile</c> — они остаются осями сетки, это
/// поверх них), пока тренд не развернётся. При здоровом тренде поведение не отличается от версии до
/// этой правки.
/// </para>
/// </summary>
public sealed class SimpleBot
{
    /// <summary>
    /// Доля <see cref="Game.Config.Session.StartingConditionsConfig.MaxInitialBuildBudget"/>,
    /// которую терпит в минусе бот с минимальным <c>leverage</c> (Блок 7.3.2) — не ноль: совсем без
    /// готовности хоть немного уйти в минус бот не может построить даже первую фабрику. Деталь
    /// реализации, не зафиксирована в доке намеренно (`docs/balancing-bots.md` §4).
    /// </summary>
    private const decimal MinInitialBuildBudgetFraction = 0.25m;


    /// <summary>
    /// На сколько ходов вперёд (не варок рецепта — поправлено тем же приёмом, что и <see
    /// cref="IdealHallCalculator"/>: настоящая желаемая потребность фабрики, <see
    /// cref="ComputeDesiredInputQuantity"/>, не плоское количество входа одной варки) бот целится
    /// держать буфер закупаемого извне сырья — заявка на покупку восполняет разницу между этим
    /// буфером и фактическим остатком. Намеренно грубая эвристика v1, не динамическая оптимизация.
    /// Снижено с 3 до 1 (запрос пользователя, rebalance/2-sector-stepwise, 2026-08-22: «бот чуть более
    /// рисковый — пусть делает запасы на меньшее количество ходов») — раньше эта же осторожность была
    /// прямой причиной того, что бот копил огромный буфер, прежде чем продать хоть что-то, теряя ходы
    /// выручки на короткой партии (см. `docs/rebalance-2sector/README.md`, разбор разрыва бот/идеал).
    /// </summary>
    private const decimal BuyBufferCycles = 1m;

    /// <summary>
    /// Симметричный буфер на столько же ходов вперёд для собственного потребления материала, который
    /// бот и производит, и мог бы продать на сторону — не оголяет свою же цепочку ради продажи:
    /// правильно посчитанная потребность (см. <see cref="BuyBufferCycles"/>) сама расставляет
    /// приоритет в пользу более глубокого, маржинального передела (запрос пользователя — «на чём бот
    /// больше заработает»), не нужен отдельный явный расчёт «что выгоднее продать». Снижено с 2 до 1
    /// — та же причина, что у <see cref="BuyBufferCycles"/>.
    /// </summary>
    private const decimal OwnUseBufferCycles = 1m;

    /// <summary>
    /// Где внутри «окна маркетмейкера» стоит МИНИМАЛЬНАЯ цена, за которую бот согласен продать в
    /// P2P: <c>0</c> — ровно столько же, сколько заплатит система, <c>1</c> — столько же, сколько
    /// стоила бы аварийная закупка. Публичная — на неё сверяется §2 диагностики.
    ///
    /// <para><b>Почему позиция в окне, а не наценка над себестоимостью</b> (блок 11.7). Раньше здесь
    /// было <c>MinSellMarginRate = 0.35</c> — «себестоимость + 35%», и корректность зависела от
    /// того, что человек вручную удержит это число строго между наценкой системной продажи (+30%) и
    /// аварийной закупки (+55%). Дважды не удерживал (найдено 2026-08-23: потолок покупателя стоял
    /// НИЖЕ пола продавца, из-за чего P2P-сделки были невыгодны в принципе и механика контрактов
    /// молча не работала). Под экзогенной ценой стало ещё хуже: единой наценки над себестоимостью
    /// больше нет вовсе, она своя у каждого материала. Позиция в окне снимает проблему
    /// структурно — любые значения из <c>[0; 1]</c> с <see cref="SellPositionInWindow"/> ниже
    /// <see cref="BuyPositionInWindow"/> дают корректный бутерброд по построению, на любом
    /// материале и в любой модели ценообразования.</para>
    ///
    /// <para>Значение 0.20 выбрано не заново: при cost-plus окно равно
    /// <c>[себестоимость × 1.30; себестоимость × 1.55]</c>, и позиция 0.20 даёт ровно
    /// <c>себестоимость × 1.35</c> — прежний <c>MinSellMarginRate = 0.35</c>, копейка в копейку.
    /// Переформулировка не меняет поведение бота, она меняет способ гарантировать его
    /// корректность.</para>
    /// </summary>
    public const decimal SellPositionInWindow = 0.20m;

    /// <summary>
    /// Где внутри окна маркетмейкера стоит МАКСИМАЛЬНАЯ цена, которую бот согласен заплатить в P2P —
    /// зеркало <see cref="SellPositionInWindow"/>. Значение 0.80 при cost-plus даёт ровно
    /// <c>себестоимость × 1.50</c>, прежний <c>MaxBuyPremiumRate = 0.50</c>.
    /// </summary>
    public const decimal BuyPositionInWindow = 0.80m;

    /// <summary>
    /// Доля неиспорченной цены, ниже которой бот не станет продавать системе прямо сейчас (блок
    /// 11.7): если залив уже просадил среднюю цену сделки ниже этого порога, остаток придерживается
    /// до следующих ходов, где давление предложения успеет затухнуть.
    ///
    /// <para>Это то самое новое решение, которого у бота не было при cost-plus: там цена не зависела
    /// от объёма вовсе, и «продать всё сразу» было строго оптимально. Под экзогенной ценой у склада
    /// появляется временная стоимость, и придерживание становится осмысленным ходом, а не
    /// нерешительностью.</para>
    ///
    /// <para>Порог применяется через <see cref="MarketSaleCalculator"/>, то есть одинаково работает
    /// в обеих моделях: при cost-plus с выключенным штрафом за превышение ёмкости цена от объёма не
    /// зависит, порог никогда не срабатывает, и поведение бота остаётся прежним.</para>
    /// </summary>
    public const decimal MinAcceptableSellPriceRate = 0.85m;

    /// <summary>Заявки мельче этого объёма не подаются вовсе — не засорять стакан пылью (Блок 7.3.1).</summary>
    private const decimal MinOrderVolume = 0.5m;

    /// <summary>Команда, за которую действует бот.</summary>
    public Ulid TeamId { get; }

    /// <summary>Сектор команды.</summary>
    public Sector Sector { get; }

    /// <summary>
    /// Подряд идущих ходов ухудшения баланса, после которого <see
    /// cref="UpdateFinancialTrend"/> начинает подрезать <see cref="_throttle"/> — запрос
    /// пользователя: «постоянно занимать бабки и при этом строить дальше — недальновидно», бот должен
    /// реагировать на тренд, а не слепо выполнять фиксированный план. Масштабируется номинальным
    /// <c>leverage</c> — чем выше аппетит к риску, тем дольше бот терпит ухудшение, прежде чем
    /// притормозить (тот же смысл, что и у остальной интерпретации оси); минимум 1 — даже самый
    /// рисковый бот не игнорирует ухудшение бесконечно.
    /// </summary>
    private int DistressThresholdTurns => 1 + (int)Math.Round(_leverage * 3m, MidpointRounding.AwayFromZero);

    /// <summary>
    /// На сколько <see cref="_throttle"/> сдвигается за один ход (к <see cref="MinThrottle"/> — при
    /// ухудшении сверх <see cref="DistressThresholdTurns"/>, обратно к 1 — при улучшении) — плавно, не
    /// рывком: полная остановка сразу после первого же лучшего хода выглядела бы так же недальновидно,
    /// как и упрямое строительство несмотря на кассовый разрыв.
    /// </summary>
    private const decimal ThrottleStep = 0.25m;

    /// <summary>
    /// Пол <see cref="_throttle"/> — раньше был 0 (полная заморозка постройки, см. doc-comment
    /// <see cref="BuildNewlyUnlockedFactories"/>), но это создавало необратимую ловушку: команда с
    /// самым ранним межотраслевым переходом в цепочке (rebalance/2-sector-stepwise, 2026-08-23,
    /// 3-секторная ступенчатая цепочка, `sector2`) проваливалась в небольшой, но НЕПРЕРЫВНЫЙ минус
    /// (доход с уже построенных фабрик чуть-чуть не покрывал растущую зарплату/содержание) — тренд
    /// баланса физически не мог «выправиться» (условие возврата к throttle=1), а без новых фабрик
    /// нарастить доход было нечем: заморожена навсегда с хода ~7 до конца 90-ходовой партии
    /// (Score(90) provider на порядок хуже даже собственного, уже отрицательного потолка идеального
    /// зала). Пол в 25% (один <see cref="ThrottleStep"/>, не «выключено вовсе») оставляет команде
    /// медленный, но работающий путь наружу — то самое реальное отличие от прежнего 0, ради которого
    /// он и введён; финансовая осторожность продолжает придерживать темп (через <see
    /// cref="UpdateInvestmentPace"/>), просто больше не запирает его насмерть.
    /// </summary>
    private const decimal MinThrottle = 0.25m;

    private readonly IReadOnlyList<FactoryDefinition> _sectorFactories;
    private readonly bool _maintainsFactories;
    private readonly decimal _leverage;
    private readonly decimal _profile;
    private readonly Action<string>? _trace;
    private decimal? _previousNetWorth;
    private int _consecutiveDeclineTurns;

    /// <summary>
    /// Множитель темпа расширения/вложений от 1 (обычное поведение по номинальным <see
    /// cref="_leverage"/>/<see cref="_profile"/>) до <see cref="MinThrottle"/> (не 0 — см. doc-comment
    /// <see cref="MinThrottle"/>) — см. <see cref="UpdateFinancialTrend"/>. 1 по умолчанию: до первого
    /// пересчёта (или если <see cref="UpdateFinancialTrend"/> вообще не вызывается вызывающим кодом)
    /// бот ведёт себя как раньше, без сюрпризов для существующих вызывающих.
    /// </summary>
    private decimal _throttle = 1m;

    /// <summary>
    /// <paramref name="maintainsFactories"/> — обслуживает ли бот износ уже построенных фабрик (SPEC
    /// §5.6, см. <see cref="MaintainFactories"/>); по умолчанию да, чтобы обычные прогоны
    /// балансировки не спотыкались о новую механику незапланированно. <c>false</c> — «пренебрегающий»
    /// вариант, нужен харнессу балансировки, чтобы проверить, что фиксированной декларации без
    /// капремонта рано или поздно перестаёт хватать (запрос пользователя: механика не должна
    /// вырождаться в «поставил и забыл» — см. doc-comment <see cref="Game.Config.Economy.WearConfig"/>).
    /// <paramref name="leverage"/>/<paramref name="profile"/> (0..1, Блок 7.3.2) — две независимые оси
    /// сетки стратегий, см. doc-comment класса; значения по умолчанию воспроизводят поведение
    /// <see cref="SimpleBot"/> до Блока 7.3.2 (регрессионный ориентир). <paramref name="trace"/> —
    /// необязательный приёмник построчных объяснений решений («строю X — хватает бюджета», «пропускаю
    /// продажу Y — не набрался минимальный объём» и т.п., Блок «трассировка ботов», rebalance/2-sector-stepwise)
    /// для диагностики (<c>--mode trace</c> в <c>Game.Balancing</c>) — <c>null</c> по умолчанию, ничего
    /// не пишет и не стоит лишних вычислений в обычных прогонах (грид на тысячи партий).
    /// </summary>
    public SimpleBot(
        Ulid teamId, Sector sector, ResolvedGameConfig config,
        bool maintainsFactories = true, decimal leverage = 1m, decimal profile = 0m, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(sector);
        ArgumentNullException.ThrowIfNull(config);
        if (leverage is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(leverage), leverage, "Leverage must be between 0 and 1.");
        }
        if (profile is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "Profile must be between 0 and 1.");
        }

        TeamId = teamId;
        Sector = sector;
        _maintainsFactories = maintainsFactories;
        _leverage = leverage;
        _profile = profile;
        _trace = trace;
        _sectorFactories = config.FactoryDefinitions
            .Where(f => f.Sector == sector)
            .OrderBy(f => f.Recipes[0].Output.Level)
            .ToList();

        if (_sectorFactories.Count == 0)
        {
            throw new ArgumentException($"Sector '{sector.Id}' has no factory definitions.", nameof(sector));
        }
    }

    /// <summary>Финальный продукт сектора этого бота — вершина его цепочки, ничем внутри неё дальше не потребляется.</summary>
    public Material FinalMaterial => _sectorFactories[^1].Recipes[0].Output;

    /// <summary>
    /// Финансовая осторожность (запрос пользователя: «постоянно строить дальше несмотря на падающий
    /// баланс — недальновидно; боты должны следить за финансовым состоянием, анализировать тренд и в
    /// зависимости от него менять поведение согласно своим параметрам»). Отслеживает тренд баланса
    /// команды. Пока баланс не ухудшается подряд дольше <see cref="DistressThresholdTurns"/> ходов —
    /// <see cref="_throttle"/> остаётся/возвращается к 1 (обычное поведение по номинальным
    /// <c>leverage</c>/<c>profile</c>). Как только порог пройден — <see cref="_throttle"/> начинает
    /// снижаться на <see cref="ThrottleStep"/> за ход, пока тренд не развернётся. Идёт во все места,
    /// где темп расширения/вложений завязан на <c>leverage</c> — <see
    /// cref="BuildNewlyUnlockedFactories"/>, <see cref="UpdateInvestmentPace"/>. Продажу через стакан
    /// (<see cref="ComputeSellOrders"/>) не трогает — источник живых денег должен работать в любом
    /// состоянии, тормозить нужно только новые траты. Вызывать первым в ходу решений, до любого из
    /// перечисленных методов, каждый ход.
    /// </summary>
    public void UpdateFinancialTrend(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var netWorth = team.Balance;

        if (_previousNetWorth is { } previous)
        {
            _consecutiveDeclineTurns = netWorth < previous ? _consecutiveDeclineTurns + 1 : 0;
        }
        _previousNetWorth = netWorth;

        var inDistress = _consecutiveDeclineTurns >= DistressThresholdTurns;
        var previousThrottle = _throttle;
        _throttle = inDistress
            ? Math.Max(MinThrottle, _throttle - ThrottleStep)
            : Math.Min(1m, _throttle + ThrottleStep);

        if (_throttle != previousThrottle)
        {
            _trace?.Invoke(inDistress
                ? $"[{Sector.Id}] throttle {previousThrottle:F2}→{_throttle:F2}: {_consecutiveDeclineTurns} ходов подряд баланс падает (порог {DistressThresholdTurns})"
                : $"[{Sector.Id}] throttle {previousThrottle:F2}→{_throttle:F2}: тренд выправился");
        }
    }

    /// <summary>
    /// Первая постройка команды (SPEC §5.1 — фабрик нет, баланс 0; никакого отдельного «стартового
    /// кредита» с иными правилами больше нет, docs/TODO.md #23: команда просто строит, баланс уходит
    /// в минус, как и от любой другой постройки в любой другой момент партии) — строит все УЖЕ
    /// разблокированные фабрики сектора, на которые хватает бюджета (Блок 9.2 — более глубокие
    /// переделы открываются постепенно, не сразу; остальные достраивает по мере разблокировки/
    /// накопления баланса <see cref="BuildNewlyUnlockedFactories"/>, тот же метод, эта функция — лишь
    /// его первый вызов). Темп вложений в R&amp;D (командный и по фабрикам) не объявляется здесь — им
    /// ведает <see cref="UpdateInvestmentPace"/>, вызываемая каждый ход решений отдельно, в том числе
    /// первый. Вызывать один раз, на первом ходу.
    /// </summary>
    public void BuildOutSectorChain(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        BuildNewlyUnlockedFactories(session);
    }

    /// <summary>
    /// Достраивает те фабрики сектора, которые ещё не построены и уже разблокированы (Блок 9.2) —
    /// на первом ходу это подмножество, доступное сразу; на последующих — то, что только что
    /// открылось благодаря командному исследованию поколений (<see cref="UpdateInvestmentPace"/>).
    /// Численность на новую фабрику и число самих фабрик берутся из <see cref="ChainCapacityPlanner"/>
    /// — не «одна фабрика с базовой численностью», как было до 2026-09-07: уровень, чей выпуск не
    /// покрывает потребность потребителей, получает донайм, а если и его потолка не хватает — вторую
    /// (третью…) фабрику той же пары (тип, рецепт). Без этого бот физически не умел расшивать узкое
    /// место, и конфиг, задуманный с расчётом на рост мощности по ходу партии, всегда выглядел
    /// сломанным (запрос пользователя; см. <c>docs/economy-accounting-audit.md</c>).
    /// R&amp;D-вложение фабрике не назначает — тем же <see cref="UpdateInvestmentPace"/>, вызванным
    /// следом в тот же ход, чтобы новая фабрика не осталась на ход без объявленного темпа.
    /// <para>
    /// Постройка не бесплатна, но и не требует отдельного оформления — баланс просто уходит в минус
    /// (docs/TODO.md #23). Тем не менее бот пропускает постройку, если она увела бы баланс глубже
    /// самостоятельно назначенной толерантности к минусу (<see
    /// cref="Game.Config.Session.StartingConditionsConfig.MaxInitialBuildBudget"/>, доля от
    /// <see cref="MinInitialBuildBudgetFraction"/> до 100% в зависимости от <c>leverage</c> — тот же
    /// диапазон, что раньше задавал размер стартового займа, теперь задаёт добровольный потолок
    /// минуса на любой ход, не только первый) — откладывает до следующего хода решений, когда баланс
    /// подрастёт продажами; разблокированный тип никуда не денется, метод идемпотентен. <see
    /// cref="_throttle"/> сюда больше не примешивается (раньше здесь была жёсткая заморозка при
    /// <c>throttle=0</c> — необратимая ловушка, см. doc-comment <see cref="MinThrottle"/>; попытка
    /// смягчить её, ещё и умножив саму толерантность на throttle, оказалась второй, накладывающейся
    /// заморозкой — команда, уже глубоко в минусе из-за прежних построек, не могла позволить себе
    /// вообще ничего нового даже на полу троттлинга; толерантность оставлена чисто leverage-зависимой,
    /// как и была, только сам бинарный запрет снят).
    /// </para>
    /// <para>
    /// Единица достройки — не <see cref="FactoryDefinition"/>, а пара (тип, рецепт) (запрос
    /// пользователя, TODO.md #20, 2026-08-17: «научим бот строить все варианты фабрик с каждым
    /// рецептом — смысл тот же, как все фабрики построить»): для типа с несколькими рецептами
    /// строится ОТДЕЛЬНАЯ фабрика на каждый рецепт (тот же принцип «построить всё разблокированное»,
    /// применённый не только к типам, но и к рецептам внутри типа), а не одна с рецептом по
    /// умолчанию (<c>Recipes[0]</c>, как было раньше — единственная причина, по которой ни один
    /// формульный бот раньше не вызывал <see cref="GameSession.SelectRecipe"/>, см.
    /// <c>docs/production-staging.md</c>, «Стадия 4»). Разблокировка проверяется ПО РЕЦЕПТУ
    /// (<c>recipe.Output.Level</c>), не по <c>Recipes[0]</c> — у типа с рецептами разных уровней они
    /// открываются независимо друг от друга.
    /// </para>
    /// </summary>
    public void BuildNewlyUnlockedFactories(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var builtCountByPair = team.Factories
            .GroupBy(f => (f.Definition.Id, f.SelectedRecipe.Id))
            .ToDictionary(group => group.Key, group => group.Count());
        var productivity = session.State.Config.Raw.WorkerProductivity;
        var factoryDefinitions = session.State.Config.Raw.FactoryDefinitions;
        var negativeBalanceTolerance = ComputeNegativeBalanceTolerance(session);
        var capacityPlan = ChainCapacityPlanner.Plan(session.State.Config);

        // Донайм на уже стоящих фабриках — раньше новых построек: он дешевле (нет BuildCost и нового
        // содержания) и закрывает то же узкое место.
        TopUpWorkersTowardPlan(session, capacityPlan, negativeBalanceTolerance);

        foreach (var definition in _sectorFactories)
        {
            foreach (var recipe in definition.Recipes)
            {
                if (recipe.Output.Level > team.UnlockedGeneration)
                {
                    continue;
                }

                var plan = capacityPlan[(definition.Id, recipe.Id)];
                var alreadyBuilt = builtCountByPair.GetValueOrDefault((definition.Id, recipe.Id));
                if (alreadyBuilt >= plan.FactoryCount)
                {
                    continue;
                }

                // Наём тоже стоит денег и тоже уводит баланс в минус — считаем его вместе с постройкой,
                // иначе бот берёт на себя расширенный штат, которого «не заметил» в своей же проверке.
                // Если на полный по плану штат денег нет, а на базовый есть — строим с тем, что тянем:
                // недобранных рабочих доберёт TopUpWorkersTowardPlan на следующих ходах, когда баланс
                // подрастёт. Отказываться от самой фабрики из-за штата было бы хуже, чем построить её
                // с базовой бригадой.
                var buildCost = factoryDefinitions.First(d => d.Id == definition.Id).BuildCost;
                var affordableWorkers = AffordableWorkerCount(
                    plan.WorkersPerFactory, alreadyHired: 0, productivity.BaseWorkerCount, productivity.HireCostPerWorker,
                    team.Balance - buildCost, negativeBalanceTolerance);
                if (affordableWorkers is null)
                {
                    _trace?.Invoke(
                        $"[{Sector.Id}] пропускаю постройку {definition.Id}/{recipe.Id} (#{alreadyBuilt + 1} из {plan.FactoryCount}): " +
                        $"баланс {team.Balance:F0} - постройка {buildCost:F0} - наём базовой бригады " +
                        $"{productivity.BaseWorkerCount * productivity.HireCostPerWorker:F0} < -толерантность {negativeBalanceTolerance:F0}");
                    continue;
                }

                var built = (FactoryBuilt)session.BuildFactory(TeamId, definition.Id, recipe.Id).Change;
                session.SetWorkerCount(TeamId, built.FactoryId, affordableWorkers.Value);
                _trace?.Invoke(
                    $"[{Sector.Id}] строю {definition.Id}/{recipe.Id} (#{alreadyBuilt + 1} из {plan.FactoryCount}): cost={buildCost:F0}, " +
                    $"баланс после={team.Balance:F0}, рабочих={affordableWorkers.Value} из {plan.WorkersPerFactory} по плану " +
                    $"(спрос {plan.DemandPerTurn:F0}/ход)");
            }
        }
    }


    /// <summary>
    /// Доводит численность уже построенных фабрик до плановой (<see cref="ChainCapacityPlanner"/>) по
    /// мере появления денег — второй из двух рычагов расширения мощности, более дешёвый (нет ни
    /// BuildCost, ни нового содержания). Вызывается каждый ход решений перед постройкой новых фабрик.
    /// Сокращать штат обратно не умеет: увольнения у бота нет как механики вовсе (docs/TODO.md №24).
    /// </summary>
    private void TopUpWorkersTowardPlan(
        GameSession session,
        IReadOnlyDictionary<(string FactoryDefinitionId, string RecipeId), ChainCapacityPlanner.RecipePlan> capacityPlan,
        decimal negativeBalanceTolerance)
    {
        var team = session.State.Teams[TeamId];
        var productivity = session.State.Config.Raw.WorkerProductivity;

        foreach (var factory in team.Factories.OrderBy(f => f.SelectedRecipe.Output.Level))
        {
            // Сравниваем с ОБЪЯВЛЕННОЙ численностью, не с фактической: наём происходит на расчёте
            // (WorkerCountSet — только объявление, см. его doc-comment), поэтому фабрика, построенная
            // этим же ходом, до расчёта имеет Workers=0 при уже объявленном плановом штате — по
            // Workers бот переобъявлял бы то же самое каждый ход и мусорил в трассировке.
            if (!capacityPlan.TryGetValue((factory.Definition.Id, factory.SelectedRecipe.Id), out var plan)
                || factory.DesiredWorkers >= plan.WorkersPerFactory)
            {
                continue;
            }

            var affordable = AffordableWorkerCount(
                plan.WorkersPerFactory, alreadyHired: factory.DesiredWorkers, minimumWorkers: factory.DesiredWorkers + 1,
                productivity.HireCostPerWorker, team.Balance, negativeBalanceTolerance);
            if (affordable is null)
            {
                continue;
            }

            session.SetWorkerCount(TeamId, factory.Id, affordable.Value);
            _trace?.Invoke(
                $"[{Sector.Id}] донайм {factory.Definition.Id}/{factory.SelectedRecipe.Id}: рабочих {factory.DesiredWorkers} -> " +
                $"{affordable.Value} (план {plan.WorkersPerFactory} под спрос {plan.DemandPerTurn:F0}/ход)");
        }
    }

    /// <summary>
    /// Наибольшая численность в диапазоне [<paramref name="minimumWorkers"/>; <paramref name="desiredWorkers"/>],
    /// доначать до которой (сверх <paramref name="alreadyHired"/>) не уводит баланс глубже
    /// толерантности. <c>null</c> — не тянем даже минимум.
    /// </summary>
    private static int? AffordableWorkerCount(
        int desiredWorkers, int alreadyHired, int minimumWorkers, decimal hireCostPerWorker,
        decimal balanceAfterOtherSpending, decimal negativeBalanceTolerance)
    {
        for (var workers = desiredWorkers; workers >= minimumWorkers; workers--)
        {
            var hireCost = (workers - alreadyHired) * hireCostPerWorker;
            if (balanceAfterOtherSpending - hireCost >= -negativeBalanceTolerance)
            {
                return workers;
            }
        }

        return null;
    }

    /// <summary>
    /// Потолок минуса, который бот готов принять ради немедленной постройки/расширения (см. doc-comment
    /// <see cref="BuildNewlyUnlockedFactories"/>) — доля <see
    /// cref="Game.Config.Session.StartingConditionsConfig.MaxInitialBuildBudget"/> от <see
    /// cref="MinInitialBuildBudgetFraction"/> (<c>leverage=0</c>) до 100% (<c>leverage=1</c>).
    /// </summary>
    private decimal ComputeNegativeBalanceTolerance(GameSession session)
    {
        var maxBudget = session.State.Config.Raw.StartingConditions.MaxInitialBuildBudget;
        var fraction = MinInitialBuildBudgetFraction + (1m - MinInitialBuildBudgetFraction) * _leverage;
        return maxBudget * fraction;
    }

    /// <summary>
    /// Держит темп вложений в R&amp;D (командное исследование поколений и каждая построенная фабрика
    /// разом, на одну и ту же долю потолка) в соответствии с осями стратегии (Блок 7.3.2, doc-comment
    /// класса): доля потолка — <c>0</c> до момента переключения, <c>leverage</c> после него. Момент
    /// переключения — <c>profile</c> доля длительности сессии (<see
    /// cref="Game.Config.Session.SessionDurationConfig.MaxTurns"/> — публично известная команде верхняя
    /// граница, не тайный <see cref="GameSessionState.EndTurn"/>), от хода 0 (<c>profile=0</c> —
    /// вкладывает с первого хода) до последнего хода сессии (<c>profile=1</c> — почти вся партия
    /// на нулевых вложениях, резкий рывок под конец). «Скромный набор фабрик» бэк-лоадед профиля
    /// (`docs/balancing-bots.md` §2) — не отдельная логика, а естественное следствие нулевого темпа
    /// командного исследования поколений: новых уровней просто не открывается, пока не наступил
    /// момент переключения. Идемпотентно (пересчитывает и переобъявляет только при расхождении с уже
    /// объявленным значением), вызывать каждый ход решений, включая первый.
    /// </summary>
    public void UpdateInvestmentPace(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var maxTurns = session.State.Config.Raw.Duration.MaxTurns;
        var switchTurn = (int)Math.Round(_profile * maxTurns, MidpointRounding.AwayFromZero);
        // _throttle=1 (по умолчанию, здоровый тренд) — точно то же значение, что и до финансовой
        // осторожности, см. doc-comment UpdateFinancialTrend.
        var fraction = (session.State.CurrentTurn >= switchTurn ? _leverage : 0m) * _throttle;

        var targetGenerationCommitment = session.State.Config.Raw.GenerationResearch.MaxCommitmentPerTurn * fraction;
        if (team.GenerationResearchCommitmentPerTurn != targetGenerationCommitment)
        {
            session.SetGenerationResearchCommitment(TeamId, targetGenerationCommitment);
        }

        var targetRndCommitment = session.State.Config.Raw.Rnd.MaxCommitmentPerTurn * fraction;
        foreach (var factory in team.Factories)
        {
            if (factory.RndCommitmentPerTurn != targetRndCommitment)
            {
                session.SetRndCommitment(TeamId, factory.Id, targetRndCommitment);
            }
        }

        _trace?.Invoke(
            $"[{Sector.Id}] темп вложений: R&D={targetRndCommitment:F0}, поколение={targetGenerationCommitment:F0} " +
            $"(leverage×throttle = {_leverage:F2}×{_throttle:F2} = {fraction:F2} от максимума)");
    }

    /// <summary>
    /// Продаёт системе излишек КАЖДОГО материала, который команда производит сама — не только вершину
    /// цепочки (FinalMaterial), как раньше. Найдено первым калибровочным прогоном `metallurgy.json`
    /// (запрос пользователя): при глубокой цепочке и без контрагента на бирже для промежуточной
    /// продукции (в партии из одинаковых самодостаточных ботов покупать её просто некому) у команды
    /// не было вообще никакого дохода, пока не достроен весь путь до флагмана — десятки ходов на
    /// нулевой выручке, реального шанса не было в принципе. Тот же излишек, что уже посчитан для
    /// биржевого стакана (<see cref="ComputeSurplus"/>) — настоящий свободный остаток сверх
    /// законтрактованного и сверх настоящей потребности собственных более глубоких переделов (<see
    /// cref="ComputeOwnUseBuffer"/>, не плоская эвристика), поэтому приоритет между «продать сейчас
    /// подешевле» и «переработать дальше подороже» не нужно считать отдельно (запрос пользователя —
    /// «на чём бот больше заработает»): правильно посчитанный буфер сам удерживает сырьё для
    /// собственного следующего передела, пока тот в нём действительно нуждается, и распродаже
    /// подлежит только то, что избыточно для ЛЮБОГО уровня своей же цепочки. Вызывать после сведения
    /// биржевого стакана (<see cref="OrderBook.Match"/>) — оставшееся после сделок с другими ботами
    /// уходит системе, не наоборот.
    /// </summary>
    public void SellSurplusToSystem(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        foreach (var material in team.Factories.Select(f => f.SelectedRecipe.Output).Distinct())
        {
            var sellable = ComputeSurplus(session, team, material);
            if (sellable <= 0)
            {
                continue;
            }

            var volume = ThrottleToAcceptablePrice(session, material, sellable);
            if (volume <= 0)
            {
                _trace?.Invoke($"[{Sector.Id}] придерживаю {material.Id}: рынок насыщен, цена ниже {MinAcceptableSellPriceRate:P0} от неиспорченной");
                continue;
            }

            session.SellToSystem(TeamId, material.Id, volume);
            _trace?.Invoke(volume < sellable
                ? $"[{Sector.Id}] продаю системе {material.Id} объём={volume:F1} из {sellable:F1} (остальное придержано — рынок насыщен)"
                : $"[{Sector.Id}] продаю системе {material.Id} объём={sellable:F1}");
        }
    }

    /// <summary>
    /// Сколько из <paramref name="desiredVolume"/> бот согласен продать системе прямо сейчас —
    /// общий с идеальным залом дроссель <see cref="MarketSaleCalculator.LargestVolumeAbovePriceFloor"/>
    /// (блок 11.7). Объём мельче <see cref="MinOrderVolume"/> не подаётся вовсе.
    /// </summary>
    private decimal ThrottleToAcceptablePrice(GameSession session, Material material, decimal desiredVolume)
    {
        var economy = session.State.Config.Raw.Economy;
        var market = session.State.Market;
        if (!market.HasQuote(material.Id))
        {
            return desiredVolume;
        }

        var supplyPressure = Engine.MarketSupplyPressureCalculator.CalculateRecentVolume(
            session.Entries, material.Id, session.State.CurrentTurn, economy);

        var volume = MarketSaleCalculator.LargestVolumeAbovePriceFloor(
            market, MaterialCosts(session), economy, material, desiredVolume, supplyPressure, MinAcceptableSellPriceRate);

        return volume >= MinOrderVolume ? volume : 0m;
    }

    /// <summary>
    /// Число самых дешёвых ступеней <see cref="Game.Config.Economy.WearConfig.OverhaulTiers"/>
    /// (упорядоченных по убыванию состояния — от «почти не изношена» к «убита»), которые бот
    /// игнорирует, прежде чем впервые заказать капремонт. Было 2 (пропускал «профилактику» и
    /// «плановое обслуживание») — снижено до 0 (запрос пользователя, rebalance/2-sector-stepwise,
    /// 2026-08-22: «ремонт на самой оптимальной стадии», после находки, что реальный бот на длинной
    /// дистанции (90 ходов) деградирует по выпуску куда сильнее идеального зала, который износ вообще
    /// не моделирует) — самая ранняя, самая дешёвая ступень («профилактика», <c>CostFraction=0.02</c>
    /// от <c>BuildCost</c>) в самой природе своей и есть «оптимальная стадия»: чинит раньше, чем
    /// накопится серьёзная просадка выпуска, и стоит в разы дешевле поздних ступеней. Ниже 0 опуститься
    /// нельзя, это уже «чинить при любом, даже нулевом, отклонении от идеала».
    /// </summary>
    private const int IgnoredCheapestTierCount = 0;

    /// <summary>
    /// Поддерживает состояние уже построенных фабрик (SPEC §5.6): заказывает капремонт, как только
    /// состояние проваливается мимо первых <see cref="IgnoredCheapestTierCount"/> (намеренно
    /// невыгодных) ступеней — не при любом, даже самом мелком, отклонении от идеала. Ничего не
    /// делает, если бот сконструирован с <c>maintainsFactories: false</c> (см. doc-comment
    /// конструктора) — специально для харнесса балансировки, которому нужен «пренебрегающий» вариант.
    /// Идемпотентно, вызывать каждый ход решений.
    /// </summary>
    public void MaintainFactories(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maintainsFactories)
        {
            return;
        }

        var team = session.State.Teams[TeamId];
        var tiers = session.State.Config.Raw.Wear.OverhaulTiers;
        foreach (var factory in team.Factories)
        {
            if (factory.IsUnderRepair || factory.OverhaulRequested || factory.Condition >= 1m)
            {
                continue;
            }

            var tier = WearCalculator.SelectTier(factory.Condition, tiers);
            var tierIndex = tier is null ? -1 : tiers.ToList().IndexOf(tier);
            if (tierIndex < IgnoredCheapestTierCount)
            {
                continue;
            }

            session.SetOverhaulRequested(TeamId, factory.Id, requested: true);
            _trace?.Invoke($"[{Sector.Id}] заказываю капремонт {factory.Definition.Id}: состояние={factory.Condition:F2}, ступень={tier?.Id ?? "?"} (cost={tier?.CostFraction:P0} от BuildCost, {tier?.DurationTurns} ход(ов))");
        }
    }

    /// <summary>
    /// Заявки на продажу для биржевого стакана (Блок 7.3.1, <see cref="OrderBook"/>) — по каждому
    /// материалу, который команда сама производит: настоящий свободный остаток сверх
    /// законтрактованного и сверх собственной потребности (<see cref="ComputeSurplus"/> — тот же
    /// расчёт, что и у <see cref="SellSurplusToSystem"/>, не оголяет свою цепочку ради продажи на
    /// сторону). Цена — себестоимость (<see cref="MaterialCosts"/>) плюс минимальная маржа (<see
    /// cref="MinSellMarginRate"/>).
    /// </summary>
    public IReadOnlyList<TradeOrder> ComputeSellOrders(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var (windowFloor, windowCeiling) = MarketMakerWindow(session);

        var orders = new List<TradeOrder>();
        foreach (var material in team.Factories.Select(f => f.SelectedRecipe.Output).Distinct())
        {
            var sellable = ComputeSurplus(session, team, material);
            if (sellable < MinOrderVolume)
            {
                _trace?.Invoke($"[{Sector.Id}] не продаю {material.Id}: излишек {sellable:F1} < минимального объёма {MinOrderVolume:F1}");
                continue;
            }
            if (!windowFloor.TryGetValue(material.Id, out var floor) || !windowCeiling.TryGetValue(material.Id, out var ceiling))
            {
                _trace?.Invoke($"[{Sector.Id}] не продаю {material.Id}: окно маркетмейкера не посчиталось");
                continue;
            }

            var limitPrice = PriceAtWindowPosition(floor, ceiling, SellPositionInWindow);
            _trace?.Invoke($"[{Sector.Id}] sellOrder {material.Id} объём={sellable:F1} окно=[{floor:F4};{ceiling:F4}] лимит={limitPrice:F4}");
            orders.Add(new TradeOrder
            {
                TeamId = TeamId,
                Material = material,
                Volume = sellable,
                LimitPrice = limitPrice,
            });
        }

        return orders;
    }

    /// <summary>
    /// Заявки на покупку для биржевого стакана (Блок 7.3.1, <see cref="OrderBook"/>) — по каждому
    /// материалу, который нужен одному из построенных рецептов команды, но не производится ею самой
    /// (то, что физически может дать только другой сектор — свои сырьё и переделы уже закрыты
    /// строительством всей цепочки сектора, см. <see cref="BuildNewlyUnlockedFactories"/>). Целится в
    /// буфер на <see cref="BuyBufferCycles"/> ходов настоящей потребности (<see
    /// cref="ComputeDesiredInputQuantity"/>, не плоское количество входа одной варки); заявка —
    /// только на нехватку до этого буфера. Цена — <see cref="BuyPositionInWindow"/> внутри окна
    /// маркетмейкера.
    /// </summary>
    public IReadOnlyList<TradeOrder> ComputeBuyOrders(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var (windowFloor, windowCeiling) = MarketMakerWindow(session);
        var ownProducedMaterials = team.Factories.Select(f => f.SelectedRecipe.Output).ToHashSet();

        var neededMaterials = team.Factories
            .SelectMany(f => f.SelectedRecipe.Inputs.Select(input => input.Material))
            .Where(material => !ownProducedMaterials.Contains(material))
            .Distinct();

        var orders = new List<TradeOrder>();
        foreach (var material in neededMaterials)
        {
            var desiredPerTurn = team.Factories.Sum(factory => ComputeDesiredInputQuantity(session, factory, material));
            var targetBuffer = desiredPerTurn * BuyBufferCycles;
            var deficit = targetBuffer - team.Warehouse.QuantityOf(material);
            if (deficit < MinOrderVolume)
            {
                _trace?.Invoke($"[{Sector.Id}] не покупаю {material.Id}: буфер {targetBuffer:F1} - склад {team.Warehouse.QuantityOf(material):F1} = {deficit:F1} < минимального объёма {MinOrderVolume:F1}");
                continue;
            }
            if (!windowFloor.TryGetValue(material.Id, out var floor) || !windowCeiling.TryGetValue(material.Id, out var ceiling))
            {
                _trace?.Invoke($"[{Sector.Id}] не покупаю {material.Id}: окно маркетмейкера не посчиталось");
                continue;
            }

            var limitPrice = PriceAtWindowPosition(floor, ceiling, BuyPositionInWindow);
            _trace?.Invoke($"[{Sector.Id}] buyOrder {material.Id} объём={deficit:F1} окно=[{floor:F4};{ceiling:F4}] лимит={limitPrice:F4}");
            orders.Add(new TradeOrder
            {
                TeamId = TeamId,
                Material = material,
                Volume = deficit,
                LimitPrice = limitPrice,
            });
        }

        return orders;
    }

    /// <summary>
    /// Настоящий свободный остаток материала команды сверх уже обещанного действующими контрактами
    /// продажи и сверх буфера на собственное потребление (<see cref="ComputeOwnUseBuffer"/>) — общий
    /// расчёт для биржевого стакана (<see cref="ComputeSellOrders"/>) и системной продажи (<see
    /// cref="SellSurplusToSystem"/>), один и тот же излишек, разные покупатели.
    /// </summary>
    private decimal ComputeSurplus(GameSession session, Team team, Material material)
    {
        var reservedByContracts = session.State.Contracts.Values
            .Where(c => c.SellerTeamId == TeamId && c.Status == ContractStatus.Active && c.Terms.Material == material)
            .Sum(c => c.Terms.Volume);

        return team.Warehouse.QuantityOf(material) - reservedByContracts - ComputeOwnUseBuffer(session, team, material);
    }

    /// <summary>
    /// Буфер на <see cref="OwnUseBufferCycles"/> ходов настоящей суммарной потребности всех фабрик
    /// команды, использующих <paramref name="material"/> как вход (<see
    /// cref="ComputeDesiredInputQuantity"/>) — материал, который нужен собственному более глубокому
    /// переделу, туда и идёт в первую очередь, а не на сторону.
    /// </summary>
    private static decimal ComputeOwnUseBuffer(GameSession session, Team team, Material material) =>
        team.Factories.Sum(factory => ComputeDesiredInputQuantity(session, factory, material)) * OwnUseBufferCycles;

    /// <summary>
    /// Сколько <paramref name="material"/> желала бы потребить за один ход конкретная <paramref
    /// name="factory"/>, если бы сырья хватало сколько угодно, — теоретический потолок выпуска (<see
    /// cref="ProductionCalculator.CalculateCapacityBreakdown"/>) делится на выход рецепта за варку и
    /// умножается на количество входа за варку. Тот же приём, что уже применяется в идеальном зале
    /// (Блок 7.3.4, <c>IdealHallCalculator</c>) — раньше здесь (и в <see cref="ComputeSellOrders"/>/
    /// <see cref="ComputeBuyOrders"/>) была плоская эвристика на количество входа одной варки рецепта
    /// без учёта реальной мощности фабрики, из-за чего буфер на собственное потребление мог оказаться
    /// заметно меньше настоящей потребности и бот распродавал сырьё, которое на самом деле было нужно
    /// его же более глубокому переделу. 0, если <paramref name="material"/> вообще не вход рецепта
    /// этой фабрики.
    /// </summary>
    private static decimal ComputeDesiredInputQuantity(GameSession session, Factory factory, Material material)
    {
        var input = factory.SelectedRecipe.Inputs.FirstOrDefault(i => i.Material == material);
        if (input is null)
        {
            return 0m;
        }

        var breakdown = ProductionCalculator.CalculateCapacityBreakdown(
            factory, session.State.Config.Raw.WorkerProductivity, session.State.Config.Raw.Rnd);
        var desiredBatches = breakdown.TheoreticalMaxOutput / factory.SelectedRecipe.OutputQuantity;
        return desiredBatches * input.Quantity;
    }

    /// <summary>
    /// Себестоимость каждого материала конфига — единая, статическая (<see
    /// cref="MaterialCostCalculator"/>, не рыночная котировка и не по своей же живой фабрике — запрос
    /// пользователя, rebalance/2-sector-stepwise, 2026-08-21: «НЕТ НИКАКОЙ РЫНОЧНОЙ ЦЕНЫ! Есть
    /// себестоимость материала, которую мы прекрасно можем посчитать»). Раньше здесь была
    /// команда-специфичная оценка (<c>FactoryProfitabilityCalculator</c> по своей фабрике, рыночная
    /// котировка как запасной вариант) — из-за этого продавец и покупатель одного и того же материала
    /// могли получить РАЗНЫЕ числа для одной и той же вещи; теперь все команды и система смотрят на
    /// одну и ту же величину, поэтому пол продавца (<see cref="MinSellMarginRate"/> над себестоимостью)
    /// заведомо ниже потолка покупателя (<see cref="MaxBuyPremiumRate"/> над той же себестоимостью) —
    /// сделка между двумя честными командами больше не может провалиться из-за рассинхрона в том, что
    /// каждая сторона считает «себестоимостью».
    /// </summary>
    private static IReadOnlyDictionary<string, decimal> MaterialCosts(GameSession session) =>
        Engine.MaterialCostCalculator.CalculateAll(session.State.Config);

    /// <summary>
    /// Границы «окна маркетмейкера» по каждому материалу: сколько за единицу заплатит система
    /// (нижняя) и сколько запросит за неё при аварийной закупке (верхняя). Всё, что между ними, —
    /// пространство, где P2P-сделка выгоднее обоих внешних вариантов сразу ОБЕИМ сторонам.
    /// <see cref="Engine.SystemSaleReferencePriceCalculator"/> знает про модель ценообразования, бот —
    /// нет: он видит только две границы (блок 11.7).
    /// </summary>
    private static (IReadOnlyDictionary<string, decimal> Floor, IReadOnlyDictionary<string, decimal> Ceiling)
        MarketMakerWindow(GameSession session)
    {
        var costs = MaterialCosts(session);
        return (
            Engine.SystemSaleReferencePriceCalculator.CalculateAll(session.State.Config, costs),
            Engine.SystemSaleReferencePriceCalculator.CalculateEmergencyAll(session.State.Config, costs));
    }

    /// <summary>Цена в заданной позиции окна: <c>пол + позиция × (потолок − пол)</c>.</summary>
    private static decimal PriceAtWindowPosition(decimal floor, decimal ceiling, decimal position) =>
        floor + position * (ceiling - floor);
}
