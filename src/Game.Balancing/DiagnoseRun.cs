using Game.Bots;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing;

/// <summary>
/// Единая диагностика цепочки — <c>--mode diagnose</c> (запрос пользователя, rebalance/2-sector-stepwise,
/// 2026-08-23: «у нас есть для этого три инструмента — аналитический, ideal-hall и simplebot, но мы их
/// пока не используем как единый инструмент, идём методом научного тыка»). Прогоняет все три уровня
/// подряд на одном конфиге и печатает не три отдельных числа, а один вердикт: цепочка проходима или
/// нет, и если нет — на каком именно уровне искать причину:
///
/// <list type="number">
/// <item><b>Себестоимость (`ProductionCostLevelCalculator`)</b> — статический срез без хода/рынка/
/// ботов. Ловит абсурдные рецепты (материал дешевле собственного сырья — <see
/// cref="FindCostAnomalies"/>) до всякой симуляции.</item>
/// <item><b>Окупаемость по уровням (<see cref="FindBadPaybackLevels"/>)</b> — направление A плана
/// исследований (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>, 2026-08-23): каждый
/// уровень обязан окупиться сам по себе при продаже 100% выпуска системе, без кросс-торговли —
/// решение пользователя, команда может не дойти до конца партии, окупаемость всей цепочки в целом
/// недостаточна как гарантия.</item>
/// <item><b>«Бутерброд наценок» (<see cref="CheckMarginSandwich"/>)</b> — статическая сверка чисел, не
/// расчёт: чтобы P2P-контракт был выгоднее рынка ОБЕИМ сторонам разом, жадность бота
/// (<see cref="SimpleBot.MinSellMarginRate"/>/<see cref="SimpleBot.MaxBuyPremiumRate"/>) должна лежать
/// строго между полом продавца (наценка системной продажи) и потолком покупателя (наценка аварийной
/// закупки) — иначе один из внешних вариантов (продать системе / закупить аварийно) всегда выгоднее
/// любой сделки с другой командой, и механика контрактов не работает не потому что её кто-то не
/// использует, а потому что числа не дают ей быть выгодной.</item>
/// <item><b>Идеальный зал (`IdealHallCalculator`)</b> — X(t) без поведения бота вообще. Если тут
/// минус или монотонное падение — дело в самих числах/рецептах, чинить бота бесполезно (см.
/// <c>docs/production-chain-calibration-lessons.md</c> §2).</item>
/// <item><b>Реальный бот (`BalancingHarness`/`SimpleBot`)</b> — Score(t) и его сходимость к X(t) по
/// ходам. Если X(t) в порядке, а сходимость плохая и не растёт — дело в поведении бота/движка
/// (тайминг, буферы), не в рецептах.</item>
/// </list>
/// </summary>
internal static class DiagnoseRun
{
    public static Task RunAsync(ResolvedGameConfig config, CliArguments cliArguments)
    {
        var preset = config.Raw.SessionPresets.Single(p => p.Id == cliArguments.PresetId);

        Console.WriteLine("=== 1. Себестоимость (без хода/рынка/ботов) ===");
        var costRows = ProductionCostLevelCalculator.Calculate(config, cliArguments.Workers);
        var anomalies = FindCostAnomalies(costRows);
        if (anomalies.Count == 0)
        {
            Console.WriteLine("Аномалий не найдено (ни один материал не дешевле собственного сырья).");
        }
        else
        {
            foreach (var anomaly in anomalies)
            {
                Console.WriteLine($"  ⚠ {anomaly}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 1b. Окупаемость по уровням (продажа 100% системе, без кросс-торговли) ===");
        // Половина самого длинного пресета — не preset.MaxTurns целиком: решение пользователя,
        // 2026-08-23, направление A плана исследований — «мы не уверены, что реально до конца дойдут
        // ребята, а так есть риск застрять в финансовой яме», окупаемость обязана уложиться с
        // запасом. Общий (не per-preset) порог — тот же смысл, что и у --mode cost-levels отдельно.
        var paybackWarningTurns = config.Raw.SessionPresets.Max(p => p.MaxTurns) / 2m;
        var badPayback = FindBadPaybackLevels(costRows, paybackWarningTurns);
        if (badPayback.Count == 0)
        {
            Console.WriteLine($"Все уровни окупаются не дольше {paybackWarningTurns:F0} ход(ов) при продаже 100% выпуска системе.");
        }
        else
        {
            foreach (var line in badPayback)
            {
                Console.WriteLine($"  ⚠ {line}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 2. Бутерброд наценок (система / P2P / аварийная закупка) ===");
        var sandwich = CheckMarginSandwich(config);
        Console.WriteLine(sandwich.Report);

        Console.WriteLine();
        Console.WriteLine("=== 3. Идеальный зал X(t) — без бота вовсе ===");
        var idealHall = IdealHallCalculator.Calculate(config, preset.MaxTurns);
        var idealVerdicts = new Dictionary<string, ChainVerdict>();
        foreach (var branch in idealHall.Branches)
        {
            var verdict = ClassifyTrajectory(branch.ValueByTurn);
            idealVerdicts[branch.SectorId] = verdict;
            Console.WriteLine($"  {branch.SectorId}: X({preset.MaxTurns})={branch.ValueByTurn[^1]:F0} — {verdict.Label}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 4. Реальный бот — Score(t) и сходимость к X(t) ===");
        var teams = new List<TeamSpec>();
        var bots = new List<SimpleBot>();
        foreach (var sector in config.Sectors)
        {
            for (var t = 0; t < cliArguments.TeamsPerSector; t++)
            {
                var teamId = Ulid.NewUlid();
                teams.Add(new TeamSpec { Id = teamId, Name = $"{sector.Id}-{t}", SectorId = sector.Id });
                bots.Add(new SimpleBot(teamId, sector, config, cliArguments.MaintainFactories, cliArguments.Leverage, cliArguments.Profile));
            }
        }

        var session = GameSession.StartWithEndTurn(config, preset.Id, preset.MaxTurns, teams);
        var metrics = BalancingHarness.RunSession(session, bots, new Random(2), idealHall);

        var averageScoreBySector = metrics.FinalScores
            .GroupBy(score => session.State.Teams[score.TeamId].Sector.Id)
            .ToDictionary(group => group.Key, group => group.Average(score => score.Score));

        foreach (var (sectorId, convergence) in metrics.FinalConvergenceBySector.OrderBy(pair => pair.Key))
        {
            var score = averageScoreBySector.GetValueOrDefault(sectorId);
            Console.WriteLine($"  {sectorId}: Score({preset.MaxTurns})={score:F0}, Score/X = {convergence:P0} (X — заведомо недостижимый потолок, низкий % — не сам по себе повод для тревоги, см. §Итоговый вердикт)");
        }

        var convergenceTrend = ClassifyConvergenceTrend(metrics.Turns);
        Console.WriteLine($"  Тренд сходимости по ходам: {convergenceTrend}");

        Console.WriteLine();
        Console.WriteLine("=== Итоговый вердикт ===");
        PrintFinalVerdict(anomalies, badPayback, sandwich, idealVerdicts, averageScoreBySector);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Сравнивает себестоимость единицы ВЫХОДА не с ценой целой единицы входа напрямую, а с её долей,
    /// приходящейся на одну единицу выхода (<c>input.Quantity / row.OutputQuantity</c>) — иначе
    /// рецепты с выходом больше 1 (например, 1 пруток даёт 20 крепежей — «дешёвая штамповка») ложно
    /// считались невозможными: 1 крепёж дешевле 1 прутка не потому что нарушена арифметика, а потому
    /// что крепёж — 1/20 прутка (найдено 2026-08-23 на реальном 4-секторном конфиге,
    /// <c>fastener-plant</c>). После нормализации проверка — истинный инвариант из самой формулы
    /// (<c>UnitCost = (InputCost + FixedCost + Electricity) / OutputQuantity ≥ InputCost / OutputQuantity</c>,
    /// а InputCost ≥ вклад любого одного входа) — сработать не должна вообще никогда на честном
    /// конфиге, но остаётся как страховка от будущей порчи формулы (отрицательный FixedCost и т.п.).
    /// </summary>
    internal static IReadOnlyList<string> FindCostAnomalies(IReadOnlyList<ProductionCostLevelCalculator.FactoryRecipeCost> rows)
    {
        var anomalies = new List<string>();
        foreach (var row in rows)
        {
            if (row.Inputs.Count == 0 || row.OutputQuantity <= 0m)
            {
                continue;
            }

            var costliestInput = row.Inputs.MaxBy(input => input.Quantity / row.OutputQuantity * input.UnitCost)!;
            var costliestInputSharePerOutputUnit = costliestInput.Quantity / row.OutputQuantity * costliestInput.UnitCost;
            if (row.UnitCost < costliestInputSharePerOutputUnit)
            {
                anomalies.Add(
                    $"{row.OutputMaterialId} (рецепт {row.RecipeId}, фабрика {row.FactoryId}): себестоимость {row.UnitCost:F4} " +
                    $"ниже, чем доля входа {costliestInput.MaterialId} на эту единицу выхода ({costliestInputSharePerOutputUnit:F4}) — " +
                    "материал не может быть дешевле доли собственного сырья, приходящейся на его единицу.");
            }
        }

        return anomalies;
    }

    /// <summary>
    /// Направление A плана исследований (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>,
    /// 2026-08-23) — уровни, чей <see cref="ProductionCostLevelCalculator.FactoryRecipeCost.PaybackTurns"/>
    /// либо не определён (никогда не окупится), либо превышает <paramref name="warningTurns"/>.
    /// Продажа 100% системе, без кросс-торговли — гарантированный пол, не оптимистичная оценка (в
    /// симметричной топологии P2P всё равно даёт команде чистый ноль, см. doc-comment класса §2).
    /// </summary>
    internal static IReadOnlyList<string> FindBadPaybackLevels(
        IReadOnlyList<ProductionCostLevelCalculator.FactoryRecipeCost> rows, decimal warningTurns)
    {
        var bad = new List<string>();
        foreach (var row in rows.OrderBy(r => r.SectorId, StringComparer.Ordinal).ThenBy(r => r.Level))
        {
            if (row.PaybackTurns is not { } payback)
            {
                bad.Add($"{row.SectorId}, уровень {row.Level}, {row.FactoryId} ({row.RecipeId}): не окупается никогда (нулевая или отрицательная маржа с продажи).");
            }
            else if (payback > warningTurns)
            {
                bad.Add($"{row.SectorId}, уровень {row.Level}, {row.FactoryId} ({row.RecipeId}): окупаемость {payback:F1} ход(ов) — дольше порога {warningTurns:F0}.");
            }
        }

        return bad;
    }

    internal sealed record MarginSandwichResult(bool IsHealthy, string Report);

    /// <summary>
    /// Продавец (в P2P) должен требовать не меньше, чем дала бы системная продажа, а покупатель — не
    /// больше, чем стоила бы аварийная закупка, иначе один из внешних вариантов всегда выгоднее любой
    /// сделки между командами и контракты как механика фактически мертвы (найдено 2026-08-23, разбор
    /// «cash flow только через маркетмейкера» в этой же сессии).
    /// </summary>
    internal static MarginSandwichResult CheckMarginSandwich(ResolvedGameConfig config)
    {
        var systemMargin = MarketSaleCalculator.SystemSaleMarginMultiplier - 1m;
        var emergencyMargin = config.Raw.Economy.EmergencyPurchaseBaseMultiplier - 1m;
        var botFloor = SimpleBot.MinSellMarginRate;
        var botCeiling = SimpleBot.MaxBuyPremiumRate;

        var lines = new List<string>
        {
            $"  Пол продавца (системная продажа): +{systemMargin:P0}",
            $"  Жадность бота в P2P: [+{botFloor:P0}; +{botCeiling:P0}]",
            $"  Потолок покупателя (аварийная закупка): +{emergencyMargin:P0}",
        };

        var problems = new List<string>();
        if (botFloor < systemMargin)
        {
            problems.Add(
                $"пол жадности бота (+{botFloor:P0}) ниже системной наценки (+{systemMargin:P0}) — продавец-бот " +
                "соглашается на P2P-сделку хуже, чем дала бы прямая продажа системе; сделки P2P систематически невыгодны продавцу.");
        }

        if (botCeiling > emergencyMargin)
        {
            problems.Add(
                $"потолок жадности бота (+{botCeiling:P0}) выше аварийной наценки (+{emergencyMargin:P0}) — покупатель-бот " +
                "готов переплатить в P2P больше, чем стоила бы аварийная закупка; проще закупиться у системы, не договариваться.");
        }

        if (botFloor > botCeiling)
        {
            problems.Add($"пол жадности (+{botFloor:P0}) выше потолка (+{botCeiling:P0}) — у бота вообще нет диапазона цены, в котором он готов и продавать, и покупать.");
        }

        if (problems.Count == 0)
        {
            lines.Add("  ✅ Бутерброд корректен: любая сделка внутри диапазона жадности бота выгоднее ОБОИХ внешних вариантов сразу.");
        }
        else
        {
            foreach (var problem in problems)
            {
                lines.Add($"  ⚠ {problem}");
            }
        }

        return new MarginSandwichResult(problems.Count == 0, string.Join(Environment.NewLine, lines));
    }

    internal sealed record ChainVerdict(bool IsPositive, bool IsRecovering, string Label);

    /// <summary>
    /// Классифицирует X(t)/Score(t)-траекторию по знаку последней точки и по тому, растёт ли вторая
    /// половина партии в среднем быстрее первой (яма в начале с восстановлением — нормально, см.
    /// <c>docs/production-chain-calibration-lessons.md</c> §7) или монотонно падает всю партию
    /// (структурная проблема чисел, не одноразовый провал на старте).
    /// </summary>
    internal static ChainVerdict ClassifyTrajectory(IReadOnlyList<decimal> valueByTurn)
    {
        var final = valueByTurn[^1];
        var isPositive = final > 0m;
        var half = valueByTurn.Count / 2;
        var isRecovering = half == 0 || valueByTurn.Skip(half).Average() >= valueByTurn.Take(half).Average();

        var label = (isPositive, isRecovering) switch
        {
            (true, true) => "положительно и растёт — здоровая траектория",
            (true, false) => "положительно на конец, но вторая половина партии хуже первой — присмотреться",
            (false, true) => "отрицательно, но восстанавливается — вероятно, просто яма в начале, нужно больше ходов или её не хватило",
            (false, false) => "отрицательно и не восстанавливается — структурная проблема чисел/рецептов, не разовый провал на старте",
        };

        return new ChainVerdict(isPositive, isRecovering, label);
    }

    private static string ClassifyConvergenceTrend(IReadOnlyList<TurnMetrics> turns)
    {
        var withConvergence = turns.Where(t => t.AverageConvergence.HasValue).ToList();
        if (withConvergence.Count < 2)
        {
            return "недостаточно данных (X(t) отрицателен почти всю партию — сходимость не считается, см. §3 выше)";
        }

        var half = withConvergence.Count / 2;
        var firstHalfAverage = withConvergence.Take(half).Average(t => t.AverageConvergence!.Value);
        var secondHalfAverage = withConvergence.Skip(half).Average(t => t.AverageConvergence!.Value);

        return secondHalfAverage >= firstHalfAverage
            ? $"растёт ({firstHalfAverage:P0} -> {secondHalfAverage:P0}) — бот со временем нагоняет идеал"
            : $"падает ({firstHalfAverage:P0} -> {secondHalfAverage:P0}) — бот со временем ОТСТАЁТ от идеала сильнее, стоит посмотреть на раскачку/буферы";
    }

    /// <summary>
    /// Раньше здесь был жёсткий порог на Score(T)/X(T) (&lt;50% — тревога) — оказался ложным
    /// срабатыванием: X(t) заведомо недостижимый потолок (ноль ошибок, ноль износа, идеальный
    /// тайминг), 7% сходимости на уже разобранной и заведомо здоровой цепочке (docs/rebalance-2sector)
    /// пугали зря. Главный, не произвольный критерий — знак самого Score(T): совпадает он с X(T) или
    /// нет (2026-08-23, запрос пользователя). Сходимость (§4) остаётся в выводе только как справочная
    /// информация, не как порог вердикта.
    /// </summary>
    private static void PrintFinalVerdict(
        IReadOnlyList<string> costAnomalies, IReadOnlyList<string> badPayback, MarginSandwichResult sandwich,
        IReadOnlyDictionary<string, ChainVerdict> idealVerdicts, IReadOnlyDictionary<string, decimal> averageScoreBySector)
    {
        if (costAnomalies.Count > 0)
        {
            Console.WriteLine("❌ ИГРАТЬ НЕЛЬЗЯ — есть аномалии себестоимости (см. §1). Чинить рецепты, дальше можно не смотреть.");
            return;
        }

        if (badPayback.Count > 0)
        {
            Console.WriteLine(
                $"❌ ИГРАТЬ НЕЛЬЗЯ — {badPayback.Count} уровень(ней) не окупается в разумный срок при продаже системе (см. §1b) — " +
                "решение пользователя (2026-08-23): окупаемость обязательна на КАЖДОМ уровне, не только у цепочки в целом " +
                "(риск застрять в финансовой яме, если команда не дойдёт до конца партии). Чинить BuildCost/FixedCostPerTurn/" +
                "ProductionRate этих уровней, дальше можно не смотреть.");
            return;
        }

        var brokenSectors = idealVerdicts.Where(pair => !pair.Value.IsPositive && !pair.Value.IsRecovering).Select(pair => pair.Key).ToList();
        if (brokenSectors.Count > 0)
        {
            Console.WriteLine(
                $"❌ ИГРАТЬ НЕЛЬЗЯ ни при какой игре — сектор(а) {string.Join(", ", brokenSectors)} монотонно падают даже " +
                "у идеального зала (§3). Дело в числах/рецептах, не в поведении бота — искать здесь, не в SimpleBot.");
            return;
        }

        // Единственный по-настоящему объективный (не произвольным числом-порогом) сигнал «дело в
        // боте/движке, не в рецептах»: потолок положительный, а реальный бот всё равно ушёл в минус —
        // идеальная стратегия выигрывает там, где настоящая проигрывает, значит разница не в числах.
        var losingSectors = idealVerdicts.Keys
            .Where(sectorId => idealVerdicts[sectorId].IsPositive && averageScoreBySector.GetValueOrDefault(sectorId) < 0m)
            .ToList();
        if (losingSectors.Count > 0)
        {
            Console.WriteLine(
                $"⚠ Потолок (X(t)) положительный, но реальный бот в секторе(ах) {string.Join(", ", losingSectors)} всё равно " +
                "закончил партию в минусе (Score(T) < 0, §4) — дело в поведении бота/движка (тайминг доставки, буферы, раскачка), " +
                "не в рецептах. Смотреть трассировку (--mode trace).");
        }
        else
        {
            Console.WriteLine("✅ Цепочка играбельна: себестоимость честная, потолок положительный, реальный бот тоже заканчивает партию в плюсе.");
        }

        if (!sandwich.IsHealthy)
        {
            Console.WriteLine("⚠ Отдельно: бутерброд наценок (§2) нарушен — P2P-контракты между командами экономически не могут обгонять маркетмейкер, даже если сама цепочка играбельна.");
        }
    }
}
