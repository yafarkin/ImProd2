namespace Game.Config.Economy;

/// <summary>
/// Масштабирует шесть именованных рычагов баланса (<c>docs/difficulty.md</c> §2-§3 — было восемь:
/// рычаг ставки по займу убран вместе с самим банковским займом как классом механики (docs/TODO.md
/// #23), рычаг эскалации командной зарплаты убран вместе с самой прогрессией
/// (Game.Engine.FinanceCalculator — не различала эффективный найм от раздутого при реалистичном числе
/// фабрик, дублировала убывающую отдачу выработки без своей пользы))
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
/// сложности.
///
/// Анкеры пересчитаны 2026-09-07 (docs/TODO.md №30, docs/difficulty.md §8): весь диапазон 0–5 теперь
/// проходит <c>--mode diagnose</c> чисто на обеих боевых цепочках (<c>--difficulty</c> — прогон по
/// всем шести целым уровням одной серией). Тяжёлая сторона трёх рычагов (<see cref="BuildCostAnchors"/>,
/// <see cref="FixedCostPerTurnAnchors"/>, <see cref="ResearchPointThresholdAnchors"/>) намеренно
/// пологая: сделать её резче — значит увести крайние уровни в непроходимость (окупаемость за 83+
/// ходов, §1c-минус, разблокировка флагмана позже четверти партии). Основной «тяжёлый» вклад несут
/// <see cref="AccelerationFactorPerTurnAnchors"/> (износ ×3.0) и <see cref="ProductionRateBonusPerLevelAnchors"/>
/// (бонус R&amp;D ×0.5) — они ничем не ограничены и остались агрессивными.
/// </summary>
public static class DifficultyScaler
{
    // Каждая таблица — 6 множителей на уровни 0..5, индекс 3 = 1.0 (нейтральный уровень). Применяются
    // как множитель к уже существующему значению конфига, не как абсолютные числа, — поэтому одинаково
    // работают поверх разных production-model файлов (§5 плана). Пересчитаны 2026-09-07 (docs/difficulty.md
    // §8, docs/TODO.md №30); критерий приёмки — все шесть целых уровней дают ✅ в `--mode diagnose` на
    // обеих боевых цепочках (флаг `--difficulty` прогоняет уровень за уровнем).

    // Тяжёлая сторона пологая (×1.15 на уровне 5, не ×1.7): окупаемость = BuildCost / (0.30 × передел),
    // самый медленный уровень окупается за ~71 ход при пороге 83 — запас всего ×1.17, дальше §1b
    // краснеет разом на всех глубоких уровнях.
    private static readonly double[] BuildCostAnchors = { 0.5, 0.7, 0.85, 1.0, 1.08, 1.15 };
    // Ничем не ограничен — несёт основной «тяжёлый» вклад бегунка вместе с износом.
    private static readonly double[] ProductionRateBonusPerLevelAnchors = { 2.0, 1.5, 1.2, 1.0, 0.7, 0.5 };
    // Множит пороги И поколений, И R&D фабрик. Тяжёлая сторона почти плоская (×1.09): очки поколения ~
    // √вложений, поэтому ход разблокировки растёт как ~квадрат множителя, а правило дизайна §0
    // (ChainDesignRules) требует, чтобы флагман открывался не позже четверти партии — на обучающей
    // цепочке он открывается на 21-м ходу из 98, до стены (ход 25) остаётся ×1.09 по множителю.
    private static readonly double[] ResearchPointThresholdAnchors = { 0.4, 0.6, 0.8, 1.0, 1.05, 1.09 };
    // Рычаг «доходность передела». До 2026-09-07 здесь был BasePrice по материалам — но под
    // ценообразованием «себестоимость + фиксированная наценка» базовая цена не участвует ни в одной
    // денежной операции (и системная продажа, и аварийная закупка берут цену из
    // MaterialCostCalculator), то есть рычаг был мёртвым: бегунок двигал пять параметров из шести.
    // FixedCostPerTurn — его честная замена, потому что под cost-plus именно содержание фабрики и
    // есть задатчик прибыли: прибыль за ход = 0.30 × (содержание + электричество + зарплата).
    // Направление то же, что было у цены: сложнее — доходность ниже. Тяжёлая сторона пологая (×0.93):
    // ниже устойчивая экономика сектора B (§1c) уходит в минус — зарплата плюс вложения в поколение/
    // R&D по потолку начинают превышать суммарную прибыль. См. docs/levers.md §0.
    private static readonly double[] FixedCostPerTurnAnchors = { 1.5, 1.25, 1.1, 1.0, 0.96, 0.93 };
    // Лёгкая сторона приколочена к 1.0: опустить наценку аварийной закупки ниже потолка жадности бота
    // (+50%, SimpleBot.MaxBuyPremiumRate) — значит сломать «бутерброд наценок» (§2 диагностики),
    // покупателю станет дешевле закупиться у системы, чем договариваться. Дефолт 1.55 (+55%) уже
    // почти вплотную к этому потолку, места удешевлять нет. Тяжёлая сторона свободна — дороже можно.
    private static readonly double[] EmergencyPurchaseBaseMultiplierAnchors = { 1.0, 1.0, 1.0, 1.0, 1.2, 1.467 };
    // Ничем не ограничен — вместе с ProductionRateBonusPerLevel несёт основной «тяжёлый» вклад.
    private static readonly double[] AccelerationFactorPerTurnAnchors = { 0.125, 0.375, 0.625, 1.0, 1.75, 3.0 };

    /// <summary>
    /// Возвращает новый <see cref="GameConfig"/> с применёнными множителями шести рычагов на
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
