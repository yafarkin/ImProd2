using System.Globalization;
using Game.Balancing;
using Game.Bots;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

// Блок 7.3.3 (BUILD_PLAN, docs/TODO.md №13): один вызов — одна production-model цепочка целиком (все
// секторы, которые в ней описаны, в одной партии), выбранная через --config или короткий
// интерактивный список (ConfigSelector) — без хардкода секторов A/Б прежних блоков. Блок 7.3.2
// (docs/balancing-bots.md §2): по-прежнему прогоняет сетку leverage×profile, не одну стратегию.
var cliArguments = CliArguments.Parse(args);
var config = ConfigSelector.Load(cliArguments);
var preset = config.Raw.SessionPresets.Single(p => p.Id == cliArguments.PresetId);

if (cliArguments.TeamsPerSector <= 0)
{
    throw new ArgumentException("'--teams-per-sector' must be positive.", nameof(cliArguments));
}

var sectorNames = string.Join(", ", config.Sectors.Select(s => $"{s.Id} ({s.Name})"));
Console.WriteLine($"Секторов в цепочке: {config.Sectors.Count} [{sectorNames}], команд на сектор: {cliArguments.TeamsPerSector}.");

if (cliArguments.Mode == RunMode.IdealHall)
{
    await RunIdealHallAsync(config, preset.MaxTurns, cliArguments.OutPath);
    return;
}

var leverageLevels = StrategyGridRunner.UniformLevels(cliArguments.GridSteps);
var profileLevels = StrategyGridRunner.UniformLevels(cliArguments.GridSteps);
var totalCells = leverageLevels.Count * profileLevels.Count;

Console.WriteLine($"Сетка стратегий: {leverageLevels.Count}×{profileLevels.Count} = {totalCells} ячеек, по {cliArguments.SessionsPerCell} партий на ячейку.");
Console.WriteLine("Ход занимает часы без вмешательства — ниже периодический heartbeat, не полный лог.");
Console.WriteLine();

var lastHeartbeatAt = TimeSpan.Zero;
var results = StrategyGridRunner.Run(leverageLevels, profileLevels, cliArguments.SessionsPerCell, (leverage, profile, sessionIndex) =>
{
    var teams = new List<TeamSpec>();
    var bots = new List<SimpleBot>();
    foreach (var sector in config.Sectors)
    {
        for (var t = 0; t < cliArguments.TeamsPerSector; t++)
        {
            var teamId = Ulid.NewUlid();
            teams.Add(new TeamSpec { Id = teamId, Name = $"{sector.Id}-{t}", SectorId = sector.Id });
            bots.Add(new SimpleBot(teamId, sector, config, cliArguments.MaintainFactories, leverage, profile));
        }
    }

    // Зерно жеребьёвки хода окончания и решений ботов зависит и от ячейки, и от номера партии внутри
    // неё — иначе все ячейки играли бы на одном и том же наборе партий, что скрыло бы часть разброса.
    var seed = (int)(leverage * 1000) * 100_000 + (int)(profile * 1000) * 1000 + sessionIndex;
    var session = GameSession.Start(config, preset, teams, new Random(seed + 1));
    return (session, (IReadOnlyList<SimpleBot>)bots, new Random(seed + 1_000_000));
}, progress =>
{
    var isBoundary = progress.SessionIndex == 1 || progress.SessionIndex == progress.SessionsPerCell;
    if (!isBoundary && progress.Elapsed - lastHeartbeatAt < TimeSpan.FromSeconds(5))
    {
        return;
    }

    lastHeartbeatAt = progress.Elapsed;
    Console.WriteLine(
        $"[{progress.Elapsed:hh\\:mm\\:ss}] ячейка {progress.CellIndex}/{progress.TotalCells} " +
        $"(leverage={progress.Leverage:0.00}, profile={progress.Profile:0.00}) — " +
        $"партия {progress.SessionIndex}/{progress.SessionsPerCell}");
});

Console.WriteLine();
Console.WriteLine("Leverage Profile  Доля дефолтов  Доля вын.ремонтов  Ср.разброс итоговых счетов");
foreach (var cell in results)
{
    Console.WriteLine(
        $"{cell.Leverage,8:0.00} {cell.Profile,7:0.00} {cell.Report.ForcedLoanShare,14:P1} " +
        $"{cell.Report.ForcedRepairEventShare,18:P1} {cell.Report.AverageFinalScoreSpread,26:N0}");
}

await using (var writer = new StreamWriter(cliArguments.OutPath))
{
    await writer.WriteLineAsync("Leverage,Profile,SessionCount,ForcedLoanShare,ForcedRepairEventShare,AverageFinalScoreSpread");
    foreach (var cell in results)
    {
        await writer.WriteLineAsync(string.Join(',',
            cell.Leverage.ToString(CultureInfo.InvariantCulture),
            cell.Profile.ToString(CultureInfo.InvariantCulture),
            cell.Report.SessionCount.ToString(CultureInfo.InvariantCulture),
            cell.Report.ForcedLoanShare.ToString(CultureInfo.InvariantCulture),
            cell.Report.ForcedRepairEventShare.ToString(CultureInfo.InvariantCulture),
            cell.Report.AverageFinalScoreSpread.ToString(CultureInfo.InvariantCulture)));
    }
}

Console.WriteLine();
Console.WriteLine($"CSV сводки по сетке записан: {Path.GetFullPath(cliArguments.OutPath)}");

/// <summary>
/// Режим --mode ideal-hall (Блок 7.3.4, docs/production-balance.md §4): X(t) по каждому сектору
/// цепочки, детерминированный расчёт, без ботов и без сетки — консольная таблица + CSV с рядом по
/// ходам на каждую ветку (тот же формат «Turn,SectorA,SectorB,...», что и прежний per-turn CSV блока
/// 7.2, только на секторы, а не на усреднённые метрики партий).
/// </summary>
static async Task RunIdealHallAsync(ResolvedGameConfig config, int maxTurns, string outPath)
{
    Console.WriteLine($"Идеальный зал: {maxTurns} ходов (MaxTurns пресета — публично известная верхняя граница, не тайный EndTurn).");
    Console.WriteLine();

    var result = IdealHallCalculator.Calculate(config, maxTurns);

    var header = "Ход " + string.Join(' ', result.Branches.Select(b => $"{b.SectorId,14}"));
    Console.WriteLine(header);
    for (var turn = 0; turn < maxTurns; turn++)
    {
        var row = $"{turn + 1,4} " + string.Join(' ', result.Branches.Select(b => $"{b.ValueByTurn[turn],14:N0}"));
        Console.WriteLine(row);
    }

    await using var writer = new StreamWriter(outPath);
    await writer.WriteLineAsync("Turn," + string.Join(',', result.Branches.Select(b => b.SectorId)));
    for (var turn = 0; turn < maxTurns; turn++)
    {
        var row = (turn + 1).ToString(CultureInfo.InvariantCulture) + "," +
                   string.Join(',', result.Branches.Select(b => b.ValueByTurn[turn].ToString(CultureInfo.InvariantCulture)));
        await writer.WriteLineAsync(row);
    }

    Console.WriteLine();
    Console.WriteLine($"CSV с X(t) по каждой ветке записан: {Path.GetFullPath(outPath)}");
}
