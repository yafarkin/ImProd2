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

    /// <summary>Что считать этим запуском (Блок 7.3.4) — сетку ботовых стратегий или идеальный зал.</summary>
    public RunMode Mode { get; init; } = RunMode.Grid;

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
                _ => throw new ArgumentException($"Unknown argument '{flag}'. Known flags: --config, --session, --preset, --sessions-per-cell, --grid-steps, --teams-per-sector, --maintain-factories, --out, --mode."),
            };
        }

        return result;
    }

    private static RunMode ParseMode(string value) => value switch
    {
        "grid" => RunMode.Grid,
        "ideal-hall" => RunMode.IdealHall,
        _ => throw new ArgumentException($"Unknown '--mode' value '{value}'. Expected 'grid' or 'ideal-hall'."),
    };
}

/// <summary>Режим прогона утилиты (Блок 7.3.4).</summary>
internal enum RunMode
{
    /// <summary>Сетка ботовых стратегий leverage×profile на реальном движке (Блок 7.3.2) — прежнее поведение по умолчанию.</summary>
    Grid,

    /// <summary>Идеальный зал X(t) (Блок 7.3.4, <c>docs/production-balance.md</c> §4) — детерминированный расчёт без ботов.</summary>
    IdealHall,
}
