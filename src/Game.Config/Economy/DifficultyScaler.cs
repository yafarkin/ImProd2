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
/// <para><b>Два рычага из шести зависят от модели ценообразования</b> (блок 11.11,
/// <c>docs/difficulty.md</c> §9): доходность и капитальные затраты. Задатчик прибыли под
/// <see cref="PricingModel.External"/> — цена (<see cref="BaseSellPriceAnchors"/>), под
/// <see cref="PricingModel.CostPlus"/> — содержание фабрики (<see cref="FixedCostPerTurnAnchors"/>);
/// применяется всегда ровно один из них. Остальные четыре рычага общие.</para>
///
/// Анкеры пересчитаны 2026-09-07 (docs/TODO.md №30, docs/difficulty.md §8) и 2026-09-08 под
/// экзогенную цену (docs/difficulty.md §9): весь диапазон 0–5 проходит <c>--mode diagnose</c> чисто
/// на обеих боевых цепочках (<c>--difficulty</c> — прогон уровня за уровнем без правки JSON).
/// Тяжёлая сторона у всех рычагов упирается в один общий бюджет — запас окупаемости §1b; резче
/// сделать её нельзя, не уведя крайние уровни в непроходимость (окупаемость за 83+ ходов, §1c-минус,
/// разблокировка флагмана позже четверти партии).
/// </summary>
public static class DifficultyScaler
{
    // Каждая таблица — 6 множителей на уровни 0..5, индекс 3 = 1.0 (нейтральный уровень). Применяются
    // как множитель к уже существующему значению конфига, не как абсолютные числа, — поэтому одинаково
    // работают поверх разных production-model файлов (§5 плана). Пересчитаны 2026-09-07 (docs/difficulty.md
    // §8, docs/TODO.md №30); критерий приёмки — все шесть целых уровней дают ✅ в `--mode diagnose` на
    // обеих боевых цепочках (флаг `--difficulty` прогоняет уровень за уровнем).

    // Капитальные затраты. Лёгкая сторона общая для обеих моделей; тяжёлая — разная, и вот почему.
    // Запас окупаемости (§1b: BuildCost / прибыль за ход ≤ 83 хода при пороге в 15 ходов до конца
    // партии) — это ОДИН общий бюджет на всю тяжёлую сторону бегунка: и подорожание стройки, и
    // урезание доходности тратят его. Под cost-plus доходность двигается слабым рычагом
    // (FixedCostPerTurn), поэтому бюджет выгоднее отдать стройке — таблица 2026-09-07 сохранена
    // без изменений. Под экзогенной ценой рычаг доходности вчетверо сильнее (см. BaseSellPriceAnchors),
    // и тот же бюджет выгоднее отдать целиком ему: ×1.15 к стройке съедал бы почти весь запас,
    // отдавая взамен несколько процентов сложности.
    private static readonly double[] BuildCostAnchorsCostPlus = { 0.5, 0.7, 0.85, 1.0, 1.08, 1.15 };
    private static readonly double[] BuildCostAnchorsExternal = { 0.5, 0.7, 0.85, 1.0, 1.0, 1.0 };
    // Ничем не ограничен — несёт основной «тяжёлый» вклад бегунка вместе с износом.
    private static readonly double[] ProductionRateBonusPerLevelAnchors = { 2.0, 1.5, 1.2, 1.0, 0.7, 0.5 };
    // Множит пороги И поколений, И R&D фабрик. Тяжёлая сторона почти плоская (×1.09): очки поколения ~
    // √вложений, поэтому ход разблокировки растёт как ~квадрат множителя, а правило дизайна §0
    // (ChainDesignRules) требует, чтобы флагман открывался не позже четверти партии — на обучающей
    // цепочке он открывается на 21-м ходу из 98, до стены (ход 25) остаётся ×1.09 по множителю.
    private static readonly double[] ResearchPointThresholdAnchors = { 0.4, 0.6, 0.8, 1.0, 1.05, 1.09 };
    // Рычаг №4 «доходность передела» задан ДВУМЯ таблицами, по одной на модель ценообразования —
    // потому что задатчик прибыли в них разный, и таблица одной модели в другой не просто слабее, а
    // работает в обратную сторону (docs/difficulty.md §9).
    //
    // Под PricingModel.External прибыль = цена − себестоимость, и задатчик — цена. Рычаг сильный:
    // при базовой марже 30% множитель цены m даёт прибыль (1.30·m − 1), то есть m = 0.90 срезает
    // маржу сырья почти вдвое. Тяжёлая сторона ограничена самым мелким переделом: добыча идёт с
    // наименьшей маржой (надбавка за глубину её не касается), и она обязана оставаться прибыльной —
    // цепочка без сырья не работает вовсе. Лёгкая сторона ничем не ограничена: дороже продавать
    // можно сколько угодно. «Бутерброд наценок» (§2 диагностики) этот рычаг не ломает ни на одном
    // краю — цена аварийной закупки считается от той же котировки и масштабируется вместе с ней.
    private static readonly double[] BaseSellPriceAnchors = { 1.14, 1.09, 1.045, 1.0, 0.978, 0.96 };
    // Под PricingModel.CostPlus цена не участвует ни в одной денежной операции (и системная продажа,
    // и аварийная закупка берут её из MaterialCostCalculator), зато прибыль за ход тождественно
    // равна 0.30 × (содержание + электричество + зарплата) — то есть задатчик прибыли там
    // содержание фабрики. Эта таблица откалибрована 2026-09-07 под cost-plus и оставлена как есть:
    // режим сохранён как калибровочно-регрессионный, менять его поведение блоком 11.11 незачем.
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

        var productionBonusMultiplier = MultiplierAt(ProductionRateBonusPerLevelAnchors, difficultyLevel);
        var researchThresholdMultiplier = MultiplierAt(ResearchPointThresholdAnchors, difficultyLevel);
        var emergencyMultiplier = MultiplierAt(EmergencyPurchaseBaseMultiplierAnchors, difficultyLevel);
        var wearAccelerationMultiplier = MultiplierAt(AccelerationFactorPerTurnAnchors, difficultyLevel);

        // Рычаг доходности — ровно один из двух, никогда оба сразу: иначе бегунок дважды двигал бы
        // одну и ту же величину, а в неродной модели ещё и в обратную сторону (под экзогенной ценой
        // рост FixedCostPerTurn — чистый убыток, тогда как под cost-plus он поднимает и цену тоже).
        var external = config.Economy.PricingModel == PricingModel.External;
        var sellPriceMultiplier = external ? MultiplierAt(BaseSellPriceAnchors, difficultyLevel) : 1m;
        var fixedCostMultiplier = external ? 1m : MultiplierAt(FixedCostPerTurnAnchors, difficultyLevel);
        var buildCostMultiplier = MultiplierAt(
            external ? BuildCostAnchorsExternal : BuildCostAnchorsCostPlus, difficultyLevel);

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
                BaseMarketPerMaterial = config.Economy.BaseMarketPerMaterial
                    .Select(m => m with { BaseSellPrice = m.BaseSellPrice * sellPriceMultiplier })
                    .ToList(),
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
