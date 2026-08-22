using System.Globalization;

namespace Game.Balancing;

/// <summary>
/// Разбор аргументов командной строки утилиты (Блок 7.3.3, BUILD_PLAN «Фаза 7») — именованные флаги
/// вместо позиционных аргументов прежних блоков (7.3.1-7.3.2): их стало слишком много, чтобы держать
/// порядок в голове, а часть (<see cref="ConfigPath"/>/<see cref="SessionPath"/>) — новая, для выбора
/// одной production-model цепочки за вызов вместо жёстко зашитого <c>gameconfig.pilot.json</c>.
/// </summary>
internal sealed record CliArguments
{
    /// <summary>
    /// Путь к конфигу — либо уже собранный <c>GameConfig</c> целиком (как <c>gameconfig.pilot.json</c>),
    /// либо файл одной production-model цепочки без сессионных параметров (<c>Samples/production-models/*.json</c>,
    /// см. <c>docs/production-staging.md</c>) — какой из двух перед нами, определяется по содержимому
    /// файла (<see cref="ConfigSelector"/>), не по этому флагу. <c>null</c> — не указан, вызывающий код
    /// обязан спросить интерактивно (<see cref="ConfigSelector.Load"/>).
    /// </summary>
    public string? ConfigPath { get; init; }

    /// <summary>
    /// Путь к файлу сессионных параметров (<c>Samples/sessions/*.json</c>) для пары с production-model
    /// цепочкой (<see cref="ConfigPath"/>). <c>null</c> — не указан явно: если <see cref="ConfigPath"/>
    /// сам по себе не полный <c>GameConfig</c>, берётся <c>Samples/sessions/pilot.json</c> по умолчанию
    /// (<see cref="ConfigSelector"/>).
    /// </summary>
    public string? SessionPath { get; init; }

    /// <summary>Пресет длительности сессии (SessionPresets.Id сессионного файла).</summary>
    public string PresetId { get; init; } = "short";

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
                "--preset" => result with { PresetId = NextValue() },
                "--sessions-per-cell" => result with { SessionsPerCell = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--grid-steps" => result with { GridSteps = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--teams-per-sector" => result with { TeamsPerSector = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--maintain-factories" => result with { MaintainFactories = bool.Parse(NextValue()) },
                "--out" => result with { OutPath = NextValue() },
                "--mode" => result with { Mode = ParseMode(NextValue()) },
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
                _ => throw new ArgumentException(
                    $"Unknown argument '{flag}'. Known flags: --config, --session, --preset, --sessions-per-cell, --grid-steps, " +
                    "--teams-per-sector, --maintain-factories, --out, --mode, --workers, --leverage, --profile, --calibrate-lever, " +
                    "--calibrate-metric, --calibrate-target, --calibrate-min, --calibrate-max, --calibrate-tolerance, --calibrate-max-iterations."),
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
        _ => throw new ArgumentException($"Unknown '--mode' value '{value}'. Expected 'grid', 'ideal-hall', 'cost-levels', 'trace' or 'calibrate'."),
    };

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
}
