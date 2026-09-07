namespace Game.Config.Economy;

/// <summary>
/// Масштабирует шесть именованных рычагов баланса (<c>docs/difficulty.md</c> §2 — было восемь: рычаг
/// ставки по займу убран вместе с самим банковским займом как классом механики (docs/TODO.md #23),
/// рычаг эскалации командной зарплаты убран вместе с самой прогрессией (Game.Engine.FinanceCalculator
/// — не различала эффективный найм от раздутого при реалистичном числе фабрик, дублировала убывающую
/// отдачу выработки без своей пользы); docs/difficulty.md пока не переписан под новое число, см. TODO)
/// по непрерывному
/// <see cref="Session.SessionConfig.DifficultyLevel"/> — 0.0 (почти нельзя проиграть) .. 5.0 (нужна
/// высокая точность решений). Каждый рычаг задан анкерной таблицей из 6 множителей (уровни 0-5;
/// индекс 3 всегда 1.0 — нейтральная точка, совпадает с текущей откалиброванной экономикой без
/// изменений, см. <c>docs/difficulty.md</c> §1). Между целыми уровнями — линейная интерполяция; вне
/// диапазона [0, 5] — clamp на краю. Чистая функция без побочных эффектов, тот же приём, что и
/// <see cref="SystemSalePriceLadderCalculator"/>.
///
/// Анкеры — код-константы, не часть JSON-конфига (открытый вопрос <c>docs/difficulty.md</c> §6 решён
/// на шаге 2 в пользу простоты): калибровка (<c>docs/difficulty.md</c> §5) правит эти числа прямо в
/// исходнике и перезапускает <c>Game.Balancing</c>, отдельного UI для них не нужно, только вход
/// сложности. Числа — заглушки, требуют калибровки, как и everything в <c>docs/difficulty.md</c> §3.
/// </summary>
public static class DifficultyScaler
{
    // Каждая таблица — 6 множителей на уровни 0..5, индекс 3 = 1.0 (нейтральный уровень). Выведены
    // из анкерных значений docs/difficulty.md §3 делением на дефолт сессионного файла на этом
    // рычаге — поэтому применяются как множитель к уже существующему значению конфига, не как
    // абсолютные числа, и одинаково работают поверх разных production-model файлов (§5 плана).
    private static readonly double[] BuildCostAnchors = { 0.5, 0.7, 0.85, 1.0, 1.3, 1.7 };
    private static readonly double[] ProductionRateBonusPerLevelAnchors = { 2.0, 1.5, 1.2, 1.0, 0.7, 0.5 };
    private static readonly double[] ResearchPointThresholdAnchors = { 0.4, 0.6, 0.8, 1.0, 1.4, 2.0 };
    // Рычаг «доходность передела». До 2026-09-07 здесь был BasePrice по материалам — но под
    // ценообразованием «себестоимость + фиксированная наценка» базовая цена не участвует ни в одной
    // денежной операции (и системная продажа, и аварийная закупка берут цену из
    // MaterialCostCalculator), то есть рычаг был мёртвым: бегунок двигал пять параметров из шести.
    // FixedCostPerTurn — его честная замена, потому что под cost-plus именно содержание фабрики и
    // есть задатчик прибыли: прибыль за ход = 0.30 × (содержание + электричество + зарплата).
    // Направление то же, что было у цены: сложнее — доходность ниже. См. docs/levers.md §0.
    private static readonly double[] FixedCostPerTurnAnchors = { 1.5, 1.25, 1.1, 1.0, 0.85, 0.7 };
    private static readonly double[] EmergencyPurchaseBaseMultiplierAnchors = { 0.667, 0.8, 0.9, 1.0, 1.2, 1.467 };
    private static readonly double[] AccelerationFactorPerTurnAnchors = { 0.125, 0.375, 0.625, 1.0, 1.75, 3.0 };

    /// <summary>
    /// Возвращает новый <see cref="GameConfig"/> с применёнными множителями восьми рычагов на
    /// заданном уровне сложности. Не валидирует и не резолвит результат — как и <see
    /// cref="SystemSalePriceLadderCalculator.Apply"/>, это обязанность вызывающего кода (<see
    /// cref="Loading.GameConfigComposer"/> вызывает это первым, до валидации).
    /// </summary>
    public static GameConfig Apply(GameConfig config, double difficultyLevel)
    {
        ArgumentNullException.ThrowIfNull(config);

        var buildCostMultiplier = MultiplierAt(BuildCostAnchors, difficultyLevel);
        var productionBonusMultiplier = MultiplierAt(ProductionRateBonusPerLevelAnchors, difficultyLevel);
        var researchThresholdMultiplier = MultiplierAt(ResearchPointThresholdAnchors, difficultyLevel);
        var fixedCostMultiplier = MultiplierAt(FixedCostPerTurnAnchors, difficultyLevel);
        var emergencyMultiplier = MultiplierAt(EmergencyPurchaseBaseMultiplierAnchors, difficultyLevel);
        var wearAccelerationMultiplier = MultiplierAt(AccelerationFactorPerTurnAnchors, difficultyLevel);

        return config with
        {
            FactoryDefinitions = config.FactoryDefinitions
                .Select(f => f with
                {
                    BuildCost = f.BuildCost * buildCostMultiplier,
                    FixedCostPerTurn = f.FixedCostPerTurn * fixedCostMultiplier,
                })
                .ToList(),
            Rnd = config.Rnd with
            {
                ProductionRateBonusPerLevel = config.Rnd.ProductionRateBonusPerLevel * productionBonusMultiplier,
                ResearchPointThresholdsByLevel = config.Rnd.ResearchPointThresholdsByLevel
                    .Select(threshold => threshold * researchThresholdMultiplier)
                    .ToList(),
            },
            GenerationResearch = config.GenerationResearch with
            {
                ResearchPointThresholdsByGeneration = config.GenerationResearch.ResearchPointThresholdsByGeneration
                    .Select(threshold => threshold * researchThresholdMultiplier)
                    .ToList(),
            },
            Economy = config.Economy with
            {
                EmergencyPurchaseBaseMultiplier = config.Economy.EmergencyPurchaseBaseMultiplier * emergencyMultiplier,
            },
            Wear = config.Wear with
            {
                AccelerationFactorPerTurn = config.Wear.AccelerationFactorPerTurn * wearAccelerationMultiplier,
            },
        };
    }

    /// <summary>Линейно интерполированный множитель на уровне <paramref name="level"/>, вне [0, 5] — clamp на краю.</summary>
    private static decimal MultiplierAt(IReadOnlyList<double> anchors, double level)
    {
        var clamped = Math.Clamp(level, 0.0, anchors.Count - 1);
        var lowerIndex = (int)Math.Floor(clamped);
        var upperIndex = Math.Min(lowerIndex + 1, anchors.Count - 1);
        var fraction = clamped - lowerIndex;
        return (decimal)(anchors[lowerIndex] + (anchors[upperIndex] - anchors[lowerIndex]) * fraction);
    }
}
