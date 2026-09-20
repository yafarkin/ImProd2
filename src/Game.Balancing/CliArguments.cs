using System.Globalization;

namespace Game.Balancing;

/// <summary>
/// Разбор аргументов командной строки утилиты (Блок 7.3.3, BUILD_PLAN «Фаза 7») — именованные флаги
/// вместо позиционных аргументов прежних блоков (7.3.1-7.3.2): их стало слишком много, чтобы держать
/// порядок в голове, а часть (<see cref="ConfigPath"/>/<see cref="SessionPath"/>) — новая, для выбора
/// одной production-model цепочки за вызов вместо жёстко зашитого <c>legacy-combined-gameconfig.json</c>.
/// </summary>
internal sealed record CliArguments
{
    /// <summary>
    /// Путь к конфигу — либо уже собранный <c>GameConfig</c> целиком (как <c>legacy-combined-gameconfig.json</c>),
    /// либо файл одной production-model цепочки без сессионных параметров (<c>Samples/production-models/*.json</c>,
    /// см. <c>docs/production-staging.md</c>) — какой из двух перед нами, определяется по содержимому
    /// файла (<see cref="ConfigSelector"/>), не по этому флагу. <c>null</c> — не указан, вызывающий код
    /// обязан спросить интерактивно (<see cref="ConfigSelector.Load"/>).
    /// </summary>
    public string? ConfigPath { get; init; }

    /// <summary>
    /// Путь к файлу сессионных параметров (<c>Samples/sessions/*.json</c>) для пары с production-model
    /// цепочкой (<see cref="ConfigPath"/>). <c>null</c> — не указан явно: если <see cref="ConfigPath"/>
    /// сам по себе не полный <c>GameConfig</c>, берётся <c>Samples/sessions/main.json</c> по умолчанию
    /// (<see cref="ConfigSelector"/>).
    /// </summary>
    public string? SessionPath { get; init; }

    /// <summary>Партий на одну ячейку сетки <c>leverage</c>×<c>profile</c>.</summary>
    public int SessionsPerCell { get; init; } = 5;

    /// <summary>Число уровней на каждую ось сетки (см. <see cref="StrategyGridRunner.UniformLevels"/>).</summary>
    public int GridSteps { get; init; } = 5;

    /// <summary>Сколько команд-ботов заводить на каждый сектор конфига — секторов может быть от 1 (стадия 1) до 4 (стадия 4), см. <c>docs/production-staging.md</c>.</summary>
    public int TeamsPerSector { get; init; } = 2;

    /// <summary>См. doc-comment конструктора <see cref="Game.Bots.SimpleBot"/>.</summary>
    public bool MaintainFactories { get; init; } = true;

    /// <summary>Путь для JSON-отчёта прогона (Блок 7.3.6, <see cref="BalancingRunReport"/>).</summary>
    public string OutPath { get; init; } = "balancing-report.json";

    /// <summary>Что считать этим запуском (Блок 7.3.4) — сетку ботовых стратегий, идеальный зал или статическую себестоимость по уровням.</summary>
    public RunMode Mode { get; init; } = RunMode.Grid;

    /// <summary>
    /// Переопределение <see cref="Game.Config.Session.SessionConfig.DifficultyLevel"/> сессионного
    /// файла (0.0–5.0) — чтобы прогнать одну и ту же цепочку на всех целых уровнях бегунка сложности
    /// одной серией вызовов, не редактируя JSON между ними (пересчёт анкеров <see
    /// cref="Game.Config.Economy.DifficultyScaler"/>, docs/TODO.md №30). <c>null</c> — брать значение
    /// из сессионного файла как есть. Применимо только к паре модель+сессия; с уже собранным целиком
    /// <c>GameConfig</c> (<see cref="ConfigSelector"/>) флаг — ошибка: слайдер сложности такой файл не
    /// трогает вовсе (<see cref="Game.Config.Loading.GameConfigComposer"/>).
    /// </summary>
    public double? DifficultyLevel { get; init; }

    /// <summary>
    /// Число рабочих, которое ставится на КАЖДУЮ фабрику при <see cref="RunMode.CostLevels"/> — единая
    /// «линейка» рабочих для сравнения себестоимости между отраслями (запрос пользователя: «10 рабочих
    /// на фабрике 1 уровня и 10 рабочих на фабрике 8 уровня — это некий коэффициент мощности выпуска»),
    /// не влияет на другие режимы.
    /// </summary>
    public int Workers { get; init; } = 10;

    /// <summary><c>leverage</c> ботов при <see cref="RunMode.Trace"/> — одна партия, не сетка (см. doc-comment конструктора <see cref="Game.Bots.SimpleBot"/>).</summary>
    public decimal Leverage { get; init; } = 1m;

    /// <summary><c>profile</c> ботов при <see cref="RunMode.Trace"/> — см. <see cref="Leverage"/>.</summary>
    public decimal Profile { get; init; }

    /// <summary>Имя рычага калибровки при <see cref="RunMode.Calibrate"/> — ключ <see cref="CalibrationLever.All"/>.</summary>
    public string? CalibrateLever { get; init; }

    /// <summary>Какую метрику подгоняет калибратор — X(T) идеального зала (по умолчанию, быстрее) или Score(T) реального бота.</summary>
    public CalibrateMetric CalibrateMetric { get; init; } = CalibrateMetric.X;

    /// <summary>Целевое значение метрики (по умолчанию 0 — «минимальный рычаг, при котором метрика не отрицательна»).</summary>
    public decimal CalibrateTarget { get; init; }

    /// <summary>Нижняя граница отрезка поиска для <see cref="CalibrateLever"/>.</summary>
    public decimal? CalibrateMin { get; init; }

    /// <summary>Верхняя граница отрезка поиска для <see cref="CalibrateLever"/>.</summary>
    public decimal? CalibrateMax { get; init; }

    /// <summary>Допуск — бисекция останавливается, когда |метрика − цель| не больше этого значения.</summary>
    public decimal CalibrateTolerance { get; init; } = 1m;

    /// <summary>Потолок числа шагов бисекции сверх двух граничных вычислений — защита от зависания при плохо подобранном допуске.</summary>
    public int CalibrateMaxIterations { get; init; } = 25;

    /// <summary>Глубина синтетической цепочки при <see cref="RunMode.Sweep"/> (направление C).</summary>
    public int SweepLevels { get; init; } = 6;

    /// <summary>
    /// BuildCost уровня 0 при <see cref="RunMode.Sweep"/> — остальные уровни растут от него по <see
    /// cref="SweepGrowthSteps"/>. Вместе с <see cref="SweepBaseFixedCostPerTurn"/> подобран так, чтобы
    /// уровень 0 (сырьё, без входов, окупаемость от growth/decay не зависит вовсе — см. doc-comment
    /// <see cref="GeometricChainSweep"/>) сам по себе укладывался в порог при growth=1 — иначе сетка
    /// вырождается (каждая ячейка ломается на уровне 0, growth его в принципе не может починить, оба
    /// расхода растут с ним одинаково, отношение не меняется).
    /// </summary>
    public decimal SweepBaseBuildCost { get; init; } = 350m;

    /// <summary>FixedCostPerTurn уровня 0 при <see cref="RunMode.Sweep"/> — растёт вместе с BuildCost (тот же коэффициент роста).</summary>
    public decimal SweepBaseFixedCostPerTurn { get; init; } = 17m;

    /// <summary>ProductionRate уровня 0 при <see cref="RunMode.Sweep"/> — остальные уровни спадают от него по <see cref="SweepDecaySteps"/>.</summary>
    public decimal SweepBaseProductionRate { get; init; } = 100m;

    /// <summary>Единиц предыдущего уровня на 1 единицу следующего — фиксировано по всей сетке (см. doc-comment <see cref="GeometricChainSweep"/>).</summary>
    public decimal SweepInputQuantityPerLevel { get; init; } = 2m;

    /// <summary>Коэффициенты роста BuildCost/FixedCostPerTurn по уровню (ось сетки) — через запятую, например <c>1.0,1.5,2.0</c>.</summary>
    public IReadOnlyList<decimal> SweepGrowthSteps { get; init; } = [1.0m, 1.5m, 2.0m, 2.5m, 3.0m];

    /// <summary>Коэффициенты спада ProductionRate по уровню (ось сетки) — через запятую, например <c>1.0,0.7,0.4</c>.</summary>
    public IReadOnlyList<decimal> SweepDecaySteps { get; init; } = [1.0m, 0.8m, 0.6m, 0.4m, 0.2m];

    /// <summary>
    /// Порог окупаемости (ходов) для направления C — здесь нет сессионного файла с пресетами, откуда
    /// <see cref="ProductionCostLevelReportWriter.DefaultPaybackWarningTurns"/> обычно берёт число,
    /// поэтому фиксированное значение по умолчанию — то же самое 75 (90-ходовая партия минус 15
    /// ходов запаса, решение пользователя, см. doc-comment <see cref="ProductionCostLevelReportWriter.DefaultPaybackBufferTurns"/>).
    /// </summary>
    public decimal SweepPaybackWarningTurns { get; init; } = 75m;

    /// <summary>Рабочих на каждой синтетической фабрике при <see cref="RunMode.Sweep"/> — тот же смысл, что <see cref="Workers"/>.</summary>
    public int SweepWorkers { get; init; } = 10;

    /// <summary>
    /// Базовая наценка над себестоимостью при <see cref="RunMode.PriceLadder"/>, долей — общая для
    /// всех уровней передела. По умолчанию 0.30: ровно та наценка, что действовала при cost-plus,
    /// поэтому лестница с параметрами по умолчанию воспроизводит нынешнюю экономику один в один.
    /// </summary>
    public decimal BaseMargin { get; init; } = 0.30m;

    /// <summary>
    /// Прибавка к наценке за каждый уровень передела при <see cref="RunMode.PriceLadder"/>, долей —
    /// та самая единственная ручка «насколько сильнее вознаграждается глубина»
    /// (<c>docs/external-economy.md</c> §4). По умолчанию 0 — сознательно: калибровка (блок 11.8)
    /// начинается с точки, тождественной прежней экономике, и поднимает эту ручку от неё.
    /// </summary>
    public decimal DepthBonusPerLevel { get; init; }

    /// <summary>
    /// Записать посчитанную лестницу обратно в файл конфига (<see cref="RunMode.PriceLadder"/>).
    /// Без этого флага режим только печатает предпросмотр и ничего не трогает — правка боевого
    /// конфига обязана быть явным намерением, а не побочным эффектом просмотра отчёта.
    /// </summary>
    public bool Apply { get; init; }

    /// <summary>Разбирает пары <c>--флаг значение</c>; неизвестный флаг или флаг без значения — <see cref="ArgumentException"/> (лучше упасть сразу, чем молча проигнорировать опечатку в многочасовом прогоне).</summary>
    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var result = new CliArguments();
        for (var i = 0; i < args.Count; i++)
        {
            var flag = args[i];

            string NextValue()
            {
                if (i + 1 >= args.Count)
                {
                    throw new ArgumentException($"Flag '{flag}' requires a value.");
                }

                return args[++i];
            }

            result = flag switch
            {
                "--config" => result with { ConfigPath = NextValue() },
                "--session" => result with { SessionPath = NextValue() },
                "--sessions-per-cell" => result with { SessionsPerCell = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--grid-steps" => result with { GridSteps = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--teams-per-sector" => result with { TeamsPerSector = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--maintain-factories" => result with { MaintainFactories = bool.Parse(NextValue()) },
                "--out" => result with { OutPath = NextValue() },
                "--mode" => result with { Mode = ParseMode(NextValue()) },
                "--difficulty" => result with { DifficultyLevel = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--workers" => result with { Workers = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--leverage" => result with { Leverage = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--profile" => result with { Profile = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--calibrate-lever" => result with { CalibrateLever = NextValue() },
                "--calibrate-metric" => result with { CalibrateMetric = ParseCalibrateMetric(NextValue()) },
                "--calibrate-target" => result with { CalibrateTarget = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--calibrate-min" => result with { CalibrateMin = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--calibrate-max" => result with { CalibrateMax = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--calibrate-tolerance" => result with { CalibrateTolerance = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--calibrate-max-iterations" => result with { CalibrateMaxIterations = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--sweep-levels" => result with { SweepLevels = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--sweep-base-build-cost" => result with { SweepBaseBuildCost = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--sweep-base-fixed-cost" => result with { SweepBaseFixedCostPerTurn = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--sweep-base-production-rate" => result with { SweepBaseProductionRate = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--sweep-input-quantity" => result with { SweepInputQuantityPerLevel = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--sweep-growth-steps" => result with { SweepGrowthSteps = ParseDecimalList(NextValue()) },
                "--sweep-decay-steps" => result with { SweepDecaySteps = ParseDecimalList(NextValue()) },
                "--sweep-payback-target" => result with { SweepPaybackWarningTurns = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--sweep-workers" => result with { SweepWorkers = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--base-margin" => result with { BaseMargin = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--depth-bonus" => result with { DepthBonusPerLevel = decimal.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--apply" => result with { Apply = true },
                _ => throw new ArgumentException(
                    $"Unknown argument '{flag}'. Known flags: --config, --session, --sessions-per-cell, --grid-steps, " +
                    "--teams-per-sector, --maintain-factories, --out, --mode, --difficulty, --workers, --leverage, --profile, --calibrate-lever, " +
                    "--calibrate-metric, --calibrate-target, --calibrate-min, --calibrate-max, --calibrate-tolerance, --calibrate-max-iterations, " +
                    "--sweep-levels, --sweep-base-build-cost, --sweep-base-fixed-cost, --sweep-base-production-rate, --sweep-input-quantity, " +
                    "--sweep-growth-steps, --sweep-decay-steps, --sweep-payback-target, --sweep-workers, " +
                    "--base-margin, --depth-bonus, --apply."),
            };
        }

        return result;
    }

    private static RunMode ParseMode(string value) => value switch
    {
        "grid" => RunMode.Grid,
        "ideal-hall" => RunMode.IdealHall,
        "cost-levels" => RunMode.CostLevels,
        "trace" => RunMode.Trace,
        "calibrate" => RunMode.Calibrate,
        "diagnose" => RunMode.Diagnose,
        "sweep" => RunMode.Sweep,
        "price-ladder" => RunMode.PriceLadder,
        _ => throw new ArgumentException(
            $"Unknown '--mode' value '{value}'. Expected 'grid', 'ideal-hall', 'cost-levels', 'trace', 'calibrate', 'diagnose', 'sweep' or 'price-ladder'."),
    };

    /// <summary>Разбирает список чисел через запятую (<c>"1.0,1.5,2.0"</c>) для осей сетки направления C.</summary>
    private static IReadOnlyList<decimal> ParseDecimalList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => decimal.Parse(part, CultureInfo.InvariantCulture))
            .ToList();

    private static CalibrateMetric ParseCalibrateMetric(string value) => value switch
    {
        "x" => CalibrateMetric.X,
        "score" => CalibrateMetric.Score,
        _ => throw new ArgumentException($"Unknown '--calibrate-metric' value '{value}'. Expected 'x' or 'score'."),
    };
}

/// <summary>Режим прогона утилиты (Блок 7.3.4).</summary>
internal enum RunMode
{
    /// <summary>Сетка ботовых стратегий leverage×profile на реальном движке (Блок 7.3.2) — прежнее поведение по умолчанию.</summary>
    Grid,

    /// <summary>Идеальный зал X(t) (Блок 7.3.4, <c>docs/production-balance.md</c> §4) — детерминированный расчёт без ботов.</summary>
    IdealHall,

    /// <summary>
    /// Статическая себестоимость по (сектор, уровень, фабрика, рецепт) при фиксированном числе
    /// рабочих на каждой фабрике — без хода, без рынка, без ботов (<see cref="ProductionCostLevelCalculator"/>).
    /// </summary>
    CostLevels,

    /// <summary>
    /// Одна партия (не сетка) — идеальный зал и настоящие боты (<see cref="CliArguments.Leverage"/>/
    /// <see cref="CliArguments.Profile"/>) прогоняются с построчной трассировкой решений в два
    /// отдельных текстовых файла (Блок «трассировка ботов», rebalance/2-sector-stepwise) — понять,
    /// «когда начинаются проблемы» и почему конкретное решение бота разошлось с идеалом, без ручного
    /// расковыривания кода на каждый такой случай (как раньше — временный, не закоммиченный код).
    /// </summary>
    Trace,

    /// <summary>
    /// Автоподбор одного параметра-рычага (<see cref="CalibrationLever"/>) методом бисекции (<see
    /// cref="Calibrator"/>, Блок «автоподбор параметров», rebalance/2-sector-stepwise, 2026-08-22) —
    /// то же самое, что человек делал руками весь этот rebalance (правка → прогон → смотреть X(t)/
    /// Score(t) → повторить), но автоматически, до заданной цели.
    /// </summary>
    Calibrate,

    /// <summary>
    /// Единая диагностика (<see cref="DiagnoseRun"/>, rebalance/2-sector-stepwise, 2026-08-23) — все
    /// три инструмента (себестоимость, идеальный зал, реальный бот) одним прогоном на одной цепочке,
    /// с одним итоговым вердиктом «играбельно или нет, и если нет — на каком уровне искать причину»,
    /// вместо ручного прогона трёх режимов по отдельности и сведения их в голове.
    /// </summary>
    Diagnose,

    /// <summary>
    /// Направление C плана исследований (<see cref="GeometricChainSweep"/>, rebalance/2-sector-stepwise,
    /// 2026-08-24) — двумерная развёртка (коэффициент роста BuildCost × коэффициент спада
    /// ProductionRate) по синтетической, не файловой цепочке: не «эта конкретная цепочка сбалансирована
    /// или нет», а «при каких сочетаниях рычагов такая ФОРМА цепочки вообще может быть сбалансирована,
    /// на любой глубине». Не читает <c>--config</c>/<c>--session</c> вовсе — цепочка целиком собирается
    /// из <c>--sweep-*</c> флагов.
    /// </summary>
    Sweep,

    /// <summary>
    /// Лестница экзогенных цен сбыта (блок 11.2, <c>docs/external-economy.md</c> §4) — считает
    /// <c>BaseSellPrice</c> каждого материала от его себестоимости и печатает отчёт «уровень /
    /// себестоимость / цена / маржа / прибыль с единицы»; с <c>--apply</c> записывает результат
    /// обратно в файл конфига. Ни хода, ни рынка, ни ботов: статический инструмент калибровки.
    /// </summary>
    PriceLadder,
}
