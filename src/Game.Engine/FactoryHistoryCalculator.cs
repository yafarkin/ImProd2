using Game.Config.Loading;

namespace Game.Engine;

/// <summary>
/// Историческая аналитика по фабрикам одной команды для графиков на /team (запрос пользователя:
/// «график по остаткам на складе» и «график по производству», по ходам) — движок нигде не хранит
/// историю сам по себе, поэтому, как и <see cref="TurnHistoryCalculator"/> для дебрифа, она
/// восстанавливается проигрыванием уже записанного журнала на копии состояния
/// (<see cref="EventLogEntry{TState}.Change"/>.<c>Apply</c>).
/// </summary>
public static class FactoryHistoryCalculator
{
    /// <summary>
    /// Четыре параллельных ряда по одной команде: <see cref="StockByMaterialId"/> и
    /// <see cref="OutputByFactoryId"/>/<see cref="ConsumedInputsByFactoryId"/> — сырые данные (что
    /// реально произвела и потребила фабрика, что реально лежит на складе), <see cref="ProfitByLevel"/> —
    /// та же оценочная методика, что уже показывает вкладка «Прибыльность» карточки фабрики сейчас
    /// (<see cref="FactoryProfitabilityCalculator"/>), просто применённая к остаткам и ценам на конец
    /// каждого прошедшего хода, а не к текущим. <see cref="OutputByFactoryId"/> и
    /// <see cref="ConsumedInputsByFactoryId"/> для одной фабрики всегда одной длины и в одном порядке
    /// ходов — оба ряда пополняются из одного и того же события <see cref="FactoryProduced"/>.
    /// </summary>
    public sealed record TeamFactoryHistory(
        IReadOnlyDictionary<string, IReadOnlyList<(int Turn, decimal Quantity)>> StockByMaterialId,
        IReadOnlyDictionary<Ulid, IReadOnlyList<(int Turn, decimal OutputQuantity)>> OutputByFactoryId,
        IReadOnlyDictionary<Ulid, IReadOnlyList<(int Turn, IReadOnlyDictionary<string, decimal> ConsumedInputs)>> ConsumedInputsByFactoryId,
        IReadOnlyDictionary<int, IReadOnlyList<(int Turn, decimal Profit)>> ProfitByLevel);

    /// <summary>Можно звать в любой момент сессии; для команды, которой ещё нет в состоянии (сессия не началась), все ряды выходят пустыми.</summary>
    public static TeamFactoryHistory Summarize(
        IReadOnlyList<EventLogEntry<GameSessionState>> entries, ResolvedGameConfig config, Ulid teamId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(config);

        var scratch = new GameSessionState(config);
        var stockByMaterialId = new Dictionary<string, List<(int Turn, decimal Quantity)>>();
        var outputByFactoryId = new Dictionary<Ulid, List<(int Turn, decimal OutputQuantity)>>();
        var consumedInputsByFactoryId = new Dictionary<Ulid, List<(int Turn, IReadOnlyDictionary<string, decimal> ConsumedInputs)>>();
        var profitByLevel = new Dictionary<int, List<(int Turn, decimal Profit)>>();
        var turn = 0;

        foreach (var entry in entries)
        {
            entry.Change.Apply(scratch);

            // Выпуск и потребление — событийный факт, а не оценка: FactoryProduced несёт уже
            // посчитанные OutputQuantity/ConsumedInputs того хода, в котором он произошёл (RunTick
            // пишет его до смены CurrentTurn на следующий), поэтому не нужно ждать границы хода, как
            // для остатков ниже.
            if (entry.Change is FactoryProduced produced && produced.TeamId == teamId)
            {
                if (!outputByFactoryId.TryGetValue(produced.FactoryId, out var series))
                {
                    series = [];
                    outputByFactoryId[produced.FactoryId] = series;
                }

                series.Add((scratch.CurrentTurn, produced.OutputQuantity));

                if (!consumedInputsByFactoryId.TryGetValue(produced.FactoryId, out var consumedSeries))
                {
                    consumedSeries = [];
                    consumedInputsByFactoryId[produced.FactoryId] = consumedSeries;
                }

                consumedSeries.Add((scratch.CurrentTurn, produced.ConsumedInputs));
            }

            if (scratch.CurrentTurn != turn)
            {
                FlushTurnSnapshot(turn, teamId, scratch, config, stockByMaterialId, profitByLevel);
                turn = scratch.CurrentTurn;
            }
        }

        FlushTurnSnapshot(turn, teamId, scratch, config, stockByMaterialId, profitByLevel);

        return new TeamFactoryHistory(
            stockByMaterialId.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(int, decimal)>)pair.Value),
            outputByFactoryId.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(int, decimal)>)pair.Value),
            consumedInputsByFactoryId.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(int, IReadOnlyDictionary<string, decimal>)>)pair.Value),
            profitByLevel.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(int, decimal)>)pair.Value));
    }

    /// <summary>
    /// Снимок на конец завершённого хода <paramref name="completedTurn"/>: реальные остатки склада
    /// команды (как их видит текущий дашборд через <c>_teamWarehouseByMaterialId</c>) и оценка
    /// прибыльности каждой фабрики по этим остаткам и рыночным ценам того момента, просуммированная
    /// по уровню пирамиды. Фабрика без рыночной котировки в этот ход просто не попадает в сумму —
    /// не считается за ноль явно (см. doc-comment <see cref="TeamFactoryHistory"/>).
    /// </summary>
    private static void FlushTurnSnapshot(
        int completedTurn, Ulid teamId, GameSessionState scratch, ResolvedGameConfig config,
        Dictionary<string, List<(int Turn, decimal Quantity)>> stockByMaterialId,
        Dictionary<int, List<(int Turn, decimal Profit)>> profitByLevel)
    {
        if (completedTurn <= 0 || !scratch.Teams.TryGetValue(teamId, out var team))
        {
            return;
        }

        foreach (var stock in team.Warehouse.Stock)
        {
            if (!stockByMaterialId.TryGetValue(stock.Material.Id, out var series))
            {
                series = [];
                stockByMaterialId[stock.Material.Id] = series;
            }

            series.Add((completedTurn, stock.Quantity));
        }

        var profitByLevelThisTurn = new Dictionary<int, decimal>();
        foreach (var factory in team.Factories)
        {
            if (!FactoryProfitabilityCalculator.TryCalculate(
                    factory, team.Factories, team.Warehouse, scratch.Market,
                    config.Raw.WorkerProductivity, config.Raw.Rnd, config.Raw.WorkerProductivity.SalaryPerWorkerPerTurn,
                    out var estimate))
            {
                continue;
            }

            var level = factory.SelectedRecipe.Output.Level;
            profitByLevelThisTurn[level] = profitByLevelThisTurn.GetValueOrDefault(level) + estimate.Profit;
        }

        foreach (var (level, profit) in profitByLevelThisTurn)
        {
            if (!profitByLevel.TryGetValue(level, out var series))
            {
                series = [];
                profitByLevel[level] = series;
            }

            series.Add((completedTurn, profit));
        }
    }
}
