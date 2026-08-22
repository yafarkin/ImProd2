using Game.Config;

namespace Game.Balancing;

/// <summary>
/// Именованный, применимый рычаг калибровки (Блок «автоподбор параметров», rebalance/2-sector-stepwise,
/// 2026-08-22) — чистая функция «конфиг + значение параметра → новый конфиг», без побочных эффектов и
/// без знания о направлении поиска (это забота <see cref="Calibrator"/>). Список — ровно те рычаги,
/// которыми вручную двигали баланс в step14-16 этой же ветки: множитель <c>BuildCost</c>, найма,
/// зарплаты, порога разблокировки поколения; абсолютное значение наценки аварийной закупки. Наценка
/// системной продажи (<c>MarketSaleCalculator.SystemSaleMarginMultiplier</c>) в списке сознательно
/// нет — она сейчас захардкожена константой прямо в движке, не поле конфига (docs/TODO.md №27), калибратору
/// в конфиге её взять неоткуда без правки кода.
/// </summary>
internal static class CalibrationLever
{
    public sealed record Definition(string Description, Func<GameConfig, decimal, GameConfig> Apply);

    public static readonly IReadOnlyDictionary<string, Definition> All = new Dictionary<string, Definition>
    {
        ["build-cost"] = new(
            "множитель BuildCost всех фабрик (1.0 = без изменений, 0.5 = вдвое дешевле постройка)",
            (config, value) => config with
            {
                FactoryDefinitions = config.FactoryDefinitions
                    .Select(f => f with { BuildCost = f.BuildCost * value })
                    .ToList(),
            }),

        ["hire-cost"] = new(
            "множитель WorkerProductivity.HireCostPerWorker",
            (config, value) => config with
            {
                WorkerProductivity = config.WorkerProductivity with
                {
                    HireCostPerWorker = config.WorkerProductivity.HireCostPerWorker * value,
                },
            }),

        ["salary"] = new(
            "множитель WorkerProductivity.SalaryPerWorkerPerTurn",
            (config, value) => config with
            {
                WorkerProductivity = config.WorkerProductivity with
                {
                    SalaryPerWorkerPerTurn = config.WorkerProductivity.SalaryPerWorkerPerTurn * value,
                },
            }),

        ["generation-threshold"] = new(
            "множитель всех порогов GenerationResearch.ResearchPointThresholdsByGeneration",
            (config, value) => config with
            {
                GenerationResearch = config.GenerationResearch with
                {
                    ResearchPointThresholdsByGeneration = config.GenerationResearch.ResearchPointThresholdsByGeneration
                        .Select(threshold => threshold * value)
                        .ToList(),
                },
            }),

        ["emergency-purchase-margin"] = new(
            "абсолютное значение EmergencyPurchaseBaseMultiplier (не множитель — само уже множитель себестоимости)",
            (config, value) => config with
            {
                Economy = config.Economy with { EmergencyPurchaseBaseMultiplier = value },
            }),
    };
}
