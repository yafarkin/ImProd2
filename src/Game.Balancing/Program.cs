using System.Globalization;
using Game.Bots;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

var configPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");
var sessionCount = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 20;
var csvPath = args.Length > 2 ? args[2] : "balancing-report.csv";
var presetId = args.Length > 3 ? args[3] : "short";
// false — «пренебрегающий» вариант ботов (SimpleBot.MaintainFactories ничего не делает): нужен,
// чтобы явно увидеть на длинной партии, что фиксированной декларации капремонта рано или поздно
// перестаёт хватать (SPEC §5.6, запрос пользователя — не должно вырождаться в «поставил и забыл»).
var maintainFactories = args.Length > 4 ? bool.Parse(args[4]) : true;

var config = GameConfigLoader.LoadFromFile(configPath);
var preset = config.Raw.SessionPresets.Single(p => p.Id == presetId);
var sectorA = config.Sectors.Single(s => s.Id == "A");
var sectorB = config.Sectors.Single(s => s.Id == "B");

var report = BalancingHarness.RunMany(sessionCount, i =>
{
    var teams = new List<TeamSpec>();
    var bots = new List<SimpleBot>();
    for (var t = 0; t < 8; t++)
    {
        var sector = t % 2 == 0 ? sectorA : sectorB;
        var teamId = Ulid.NewUlid();
        teams.Add(new TeamSpec { Id = teamId, Name = $"Бот {t}", SectorId = sector.Id });
        bots.Add(new SimpleBot(teamId, sector, config, maintainFactories));
    }

    // Только зерно жеребьёвки хода окончания меняется между партиями — сами боты полностью
    // детерминированы, так что вся наблюдаемая разница между партиями идёт от разной длины сессии.
    var session = GameSession.Start(config, preset, teams, new Random(i + 1));
    return (session, (IReadOnlyList<SimpleBot>)bots, new Random(i + 1_000_000));
});

Console.WriteLine($"Партий: {report.SessionCount}");
Console.WriteLine($"Доля дефолтов (принудительных займов на команду-ход): {report.ForcedLoanShare:P2}");
Console.WriteLine($"Доля вынужденных ремонтов (SPEC §5.6, на команду-ход): {report.ForcedRepairEventShare:P2}");
Console.WriteLine($"Средний разброс итоговых счетов между командами партии: {report.AverageFinalScoreSpread:N0}");
Console.WriteLine();
Console.WriteLine("Ход  Ср.денежная масса  Ср.объём продаж системе  Ср.состояние фабрик  Ср.фабрик на ремонте  Партий-дожило");
foreach (var turn in report.TurnsByIndex)
{
    Console.WriteLine($"{turn.Turn,4} {turn.AverageTotalCash,18:N0} {turn.AverageVolumeSoldToSystem,22:N2} {turn.AverageFactoryCondition,19:P1} {turn.AverageFactoriesUnderRepairCount,20:N2} {turn.SessionCount,14}");
}

await using (var writer = new StreamWriter(csvPath))
{
    await writer.WriteLineAsync("Turn,AverageTotalCash,AverageVolumeSoldToSystem,AverageFactoryCondition,AverageFactoriesUnderRepairCount,SessionCount");
    foreach (var turn in report.TurnsByIndex)
    {
        await writer.WriteLineAsync(string.Join(',',
            turn.Turn.ToString(CultureInfo.InvariantCulture),
            turn.AverageTotalCash.ToString(CultureInfo.InvariantCulture),
            turn.AverageVolumeSoldToSystem.ToString(CultureInfo.InvariantCulture),
            turn.AverageFactoryCondition.ToString(CultureInfo.InvariantCulture),
            turn.AverageFactoriesUnderRepairCount.ToString(CultureInfo.InvariantCulture),
            turn.SessionCount.ToString(CultureInfo.InvariantCulture)));
    }
}

Console.WriteLine();
Console.WriteLine($"CSV с денежной массой и throughput по ходам записан: {Path.GetFullPath(csvPath)}");
