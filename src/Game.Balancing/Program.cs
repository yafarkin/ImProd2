using System.Globalization;
using Game.Bots;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

// Блок 7.3.2 (BUILD_PLAN, docs/balancing-bots.md §2): прогоняет сетку leverage×profile, не одну
// фиксированную стратегию — сетка целиком заменяет прежний однократный прогон (SimpleBot по
// умолчанию, leverage=1/profile=0, — теперь просто одна из её ячеек, doc-comment SimpleBot).
var configPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");
var sessionsPerCell = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5;
var csvPath = args.Length > 2 ? args[2] : "strategy-grid.csv";
var presetId = args.Length > 3 ? args[3] : "short";
var maintainFactories = args.Length > 4 ? bool.Parse(args[4]) : true;
var gridSteps = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 5;

var config = GameConfigLoader.LoadFromFile(configPath);
var preset = config.Raw.SessionPresets.Single(p => p.Id == presetId);
var sectorA = config.Sectors.Single(s => s.Id == "A");
var sectorB = config.Sectors.Single(s => s.Id == "B");

var leverageLevels = StrategyGridRunner.UniformLevels(gridSteps);
var profileLevels = StrategyGridRunner.UniformLevels(gridSteps);
var totalCells = leverageLevels.Count * profileLevels.Count;

Console.WriteLine($"Сетка стратегий: {leverageLevels.Count}×{profileLevels.Count} = {totalCells} ячеек, по {sessionsPerCell} партий на ячейку.");
Console.WriteLine("Ход занимает часы без вмешательства — ниже периодический heartbeat, не полный лог.");
Console.WriteLine();

var lastHeartbeatAt = TimeSpan.Zero;
var results = StrategyGridRunner.Run(leverageLevels, profileLevels, sessionsPerCell, (leverage, profile, sessionIndex) =>
{
    var teams = new List<TeamSpec>();
    var bots = new List<SimpleBot>();
    for (var t = 0; t < 8; t++)
    {
        var sector = t % 2 == 0 ? sectorA : sectorB;
        var teamId = Ulid.NewUlid();
        teams.Add(new TeamSpec { Id = teamId, Name = $"Бот {t}", SectorId = sector.Id });
        bots.Add(new SimpleBot(teamId, sector, config, maintainFactories, leverage, profile));
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

await using (var writer = new StreamWriter(csvPath))
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
Console.WriteLine($"CSV сводки по сетке записан: {Path.GetFullPath(csvPath)}");
