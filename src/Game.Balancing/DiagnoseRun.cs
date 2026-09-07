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
/// <item><b>Командная устойчивая экономика (<see cref="TeamSteadyStateCalculator"/>)</b> — найдено
/// 2026-08-23 сразу после того, как первая полностью починенная по окупаемости синтетическая цепочка
/// всё равно провалила идеальный зал: окупаемость уровня сознательно не считает зарплату и вложения
/// в поколение/R&amp;D (не варьируются по сектору) — а на практике это оказались две САМЫЕ большие
/// статьи расхода. Проверяет: если бы вся цепочка сектора была уже построена и полностью
/// укомплектована, хватает ли суммарной прибыли покрыть зарплату всех рабочих и вложения по потолку.
/// Необходимое дополнение к §1b, не замена — обе проверки должны пройти разом.</item>
/// <item><b>Физический баланс спроса/предложения (<see cref="SupplyDemandCalculator"/>)</b> — §1d,
/// в плане исследований «итерация 2, §5» (2026-09-07): хватает ли выпуска каждого материала на то,
/// что просят у него соседи по рецепту. Единственная секция, которая смотрит на ФИЗИКУ, а не на
/// деньги, и поэтому блокирует вердикт раньше §1b — окупаемость уровня считается при загрузке 100%,
/// а недокормленный уровень платит содержание и зарплату полностью, выпуская долю от мощности.</item>
/// <item><b>«Бутерброд наценок» (<see cref="CheckMarginSandwich"/>)</b> — статическая сверка чисел, не
/// расчёт: чтобы P2P-контракт был выгоднее рынка ОБЕИМ сторонам разом, жадность бота
/// (<see cref="SimpleBot.SellPositionInWindow"/>/<see cref="SimpleBot.BuyPositionInWindow"/>) задана
/// как доля «окна маркетмейкера» между ценой системной продажи и ценой аварийной закупки, поэтому
/// с блока 11.7 бутерброд корректен ПО ПОСТРОЕНИЮ. Секция осталась не как проверка ручных чисел, а
/// как проверка того, что окно вообще существует: если система продаёт не дороже, чем покупает,
/// договариваться не о чем ни при каких позициях бота.</item>
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
        var duration = config.Raw.Duration;

        Console.WriteLine("=== 0. Правила дизайна цепочки (форма, а не числа) ===");
        var designFindings = ChainDesignRules.Check(config);
        if (designFindings.Count == 0)
        {
            Console.WriteLine("Нарушений не найдено: кросс-входы делят объём, последний уровень открывается вовремя, капзатраты и оборот укладываются в лимиты.");
        }
        else
        {
            foreach (var finding in designFindings)
            {
                Console.WriteLine($"  ⚠ [{finding.Rule}] {finding.Message}");
            }
        }

        Console.WriteLine();
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
        var paybackWarningTurns = ProductionCostLevelReportWriter.DefaultPaybackWarningTurns(config.Raw.Duration);
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
        Console.WriteLine("=== 1c. Командная устойчивая экономика (всё уже построено, зарплата + поколение/R&D по потолку) ===");
        var steadyStates = TeamSteadyStateCalculator.Calculate(costRows, config);
        var badSteadyStates = steadyStates.Where(s => s.NetPerTurn < 0m).ToList();
        foreach (var s in steadyStates)
        {
            var marker = s.NetPerTurn < 0m ? " ⚠" : "";
            Console.WriteLine(
                $"  {s.SectorId}: прибыль={s.ProfitPerTurn:F0}, зарплата={s.SalaryPerTurn:F0}, " +
                $"поколение={s.GenerationResearchPerTurn:F0}, R&D={s.RndPerTurn:F0} → чистыми/ход={s.NetPerTurn:F0}{marker}");
            if (s.NetPerTurn < 0m)
            {
                Console.WriteLine($"      → {s.FormatPrescription()}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 1d. Физический баланс спроса/предложения (хватает ли выпуска соседям по рецепту) ===");
        var balances = SupplyDemandCalculator.Calculate(config, cliArguments.Workers);
        var capacityPlan = ChainCapacityPlanner.Plan(config);
        var deficits = SupplyDemandCalculator.FormatDeficits(balances, capacityPlan);
        var plannedExpansion = SupplyDemandCalculator.FormatPlannedExpansion(capacityPlan, config.Raw.WorkerProductivity.BaseWorkerCount);
        var thinSlack = SupplyDemandCalculator.FormatThinSlack(balances);
        if (deficits.Count == 0)
        {
            Console.WriteLine(plannedExpansion.Count == 0
                ? "Дефицитных материалов нет: каждого хватает на то, что просят соседи по рецепту, даже без расширения мощности."
                : "Неустранимых дефицитов нет: там, где базового выпуска не хватает, разрыв закрывается доньмом/второй фабрикой (см. ниже).");
        }
        else
        {
            foreach (var line in deficits)
            {
                Console.WriteLine($"  ⚠ {line}");
            }
        }

        foreach (var line in plannedExpansion)
        {
            Console.WriteLine($"  · план расширения: {line}");
        }

        foreach (var line in thinSlack)
        {
            Console.WriteLine($"  · {line}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 2. Бутерброд наценок (система / P2P / аварийная закупка) ===");
        var sandwich = CheckMarginSandwich(config);
        Console.WriteLine(sandwich.Report);

        Console.WriteLine();
        Console.WriteLine("=== 3. Идеальный зал X(t) — без бота вовсе ===");
        var idealHall = IdealHallCalculator.Calculate(config, duration.MaxTurns);
        var idealVerdicts = new Dictionary<string, ChainVerdict>();
        foreach (var branch in idealHall.Branches)
        {
            var verdict = ClassifyTrajectory(branch.ValueByTurn);
            idealVerdicts[branch.SectorId] = verdict;
            Console.WriteLine($"  {branch.SectorId}: X({duration.MaxTurns})={branch.ValueByTurn[^1]:F0} — {verdict.Label}");
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

        var session = GameSession.StartWithEndTurn(config, duration.MaxTurns, teams);
        var metrics = BalancingHarness.RunSession(session, bots, new Random(2), idealHall);

        var averageScoreBySector = metrics.FinalScores
            .GroupBy(score => session.State.Teams[score.TeamId].Sector.Id)
            .ToDictionary(group => group.Key, group => group.Average(score => score.Score));

        foreach (var (sectorId, convergence) in metrics.FinalConvergenceBySector.OrderBy(pair => pair.Key))
        {
            var score = averageScoreBySector.GetValueOrDefault(sectorId);
            Console.WriteLine($"  {sectorId}: Score({duration.MaxTurns})={score:F0}, Score/X = {convergence:P0} (X — заведомо недостижимый потолок, низкий % — не сам по себе повод для тревоги, см. §Итоговый вердикт)");
        }

        var convergenceTrend = ClassifyConvergenceTrend(metrics.Turns);
        Console.WriteLine($"  Тренд сходимости по ходам: {convergenceTrend}");

        Console.WriteLine();
        Console.WriteLine("=== Итоговый вердикт ===");
        PrintFinalVerdict(anomalies, badPayback, deficits, badSteadyStates, sandwich, idealVerdicts, averageScoreBySector, designFindings);

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
                bad.Add(
                    $"{row.SectorId}, уровень {row.Level}, {row.FactoryId} ({row.RecipeId}): окупаемость {payback:F1} ход(ов) — " +
                    $"дольше порога {warningTurns:F0}. Направление B: снизить BuildCost с {row.BuildCost:F0} до " +
                    $"≤{row.MaxBuildCostForTargetPayback(warningTurns):F0}, чтобы уложиться (при неизменном ProductionRate).");
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
        // Окно маркетмейкера по каждому материалу: сколько за единицу заплатит система и сколько
        // запросит за неё аварийно. Всё между границами — пространство P2P. При cost-plus окно
        // пропорционально себестоимости и потому одинаково по всей цепочке; при экзогенной цене оно
        // своё у каждого материала, поэтому проверять надо КАЖДЫЙ, а не одно общее число
        // (блок 11.6).
        var materialCosts = MaterialCostCalculator.CalculateAll(config);
        var sellPrices = SystemSaleReferencePriceCalculator.CalculateAll(config, materialCosts);
        var emergencyPrices = SystemSaleReferencePriceCalculator.CalculateEmergencyAll(config, materialCosts);

        var sellPosition = SimpleBot.SellPositionInWindow;
        var buyPosition = SimpleBot.BuyPositionInWindow;

        var lines = new List<string>
        {
            $"  Позиция бота в окне маркетмейкера: продажа {sellPosition:P0}, покупка {buyPosition:P0}",
        };

        var problems = new List<string>();

        // Бот назначает цены как долю окна (блок 11.7), поэтому «пол выше потолка» больше не может
        // возникнуть от рассинхрона чисел — только если позиции перепутаны местами.
        if (sellPosition >= buyPosition)
        {
            problems.Add(
                $"позиция продажи ({sellPosition:P0}) не ниже позиции покупки ({buyPosition:P0}) — у бота нет диапазона, " +
                "в котором он готов и продавать, и покупать. → Развести SimpleBot.SellPositionInWindow и BuyPositionInWindow.");
        }

        if (sellPosition < 0m || buyPosition > 1m)
        {
            problems.Add(
                $"позиции бота выходят за окно [0%; 100%] — сделка вне окна всегда проигрывает внешнему варианту " +
                "(продаже системе или аварийной закупке). → Вернуть SellPositionInWindow/BuyPositionInWindow внутрь [0; 1].");
        }

        // Вырожденное окно: система покупает не дешевле, чем продаёт. Договариваться тогда не о чем
        // ни при каких позициях бота.
        var degenerate = sellPrices
            .Where(pair => emergencyPrices.TryGetValue(pair.Key, out var ceiling) && ceiling <= pair.Value)
            .Select(pair => pair.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (degenerate.Count > 0)
        {
            problems.Add(
                $"окно маркетмейкера схлопнуто на материалах: {string.Join(", ", degenerate.Take(5))}" +
                (degenerate.Count > 5 ? $" и ещё {degenerate.Count - 5}" : string.Empty) +
                " — система продаёт не дороже, чем покупает, P2P-торговля бессмысленна в принципе. " +
                $"→ Поднять Economy.EmergencyPurchaseBaseMultiplier (сейчас {config.Raw.Economy.EmergencyPurchaseBaseMultiplier:F2}).");
        }

        var widths = sellPrices
            .Where(pair => emergencyPrices.ContainsKey(pair.Key) && pair.Value > 0m)
            .Select(pair => emergencyPrices[pair.Key] / pair.Value - 1m)
            .ToList();

        if (widths.Count > 0)
        {
            lines.Insert(0, widths.Max() - widths.Min() > 0.005m
                ? $"  Ширина окна (аварийная закупка над системной продажей): +{widths.Min():P0}..+{widths.Max():P0}"
                : $"  Ширина окна (аварийная закупка над системной продажей): +{widths.Min():P0}");
        }

        if (problems.Count == 0)
        {
            lines.Add(
                "  ✅ Бутерброд корректен по построению: бот назначает цены как долю окна, поэтому любая его сделка " +
                "выгоднее ОБОИХ внешних вариантов сразу — на каждом материале и в любой модели ценообразования.");
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
    internal static void PrintFinalVerdict(
        IReadOnlyList<string> costAnomalies, IReadOnlyList<string> badPayback, IReadOnlyList<string> supplyDeficits,
        IReadOnlyList<TeamSteadyStateCalculator.SectorSteadyState> badSteadyStates, MarginSandwichResult sandwich,
        IReadOnlyDictionary<string, ChainVerdict> idealVerdicts, IReadOnlyDictionary<string, decimal> averageScoreBySector,
        IReadOnlyList<ChainDesignRules.Finding>? designFindings = null)
    {
        // Печатается ПЕРЕД любым вердиктом, а не вместо него: нарушение правила дизайна само по себе
        // не делает цепочку неиграбельной, но почти всегда объясняет, почему не сходятся числа ниже,
        // и чинится раньше и дешевле, чем подбор коэффициентов.
        if (designFindings is { Count: > 0 })
        {
            Console.WriteLine(
                $"⚠ Сначала посмотрите §0: нарушено правил дизайна — {designFindings.Count} " +
                $"({string.Join(", ", designFindings.Select(f => f.Rule).Distinct())}). " +
                "Это форма цепочки, а не её числа; чинится раньше и дешевле любых коэффициентов.");
        }

        if (costAnomalies.Count > 0)
        {
            Console.WriteLine("❌ ИГРАТЬ НЕЛЬЗЯ — есть аномалии себестоимости (см. §1). Чинить рецепты, дальше можно не смотреть.");
            return;
        }

        if (supplyDeficits.Count > 0)
        {
            Console.WriteLine(
                $"❌ ИГРАТЬ НЕЛЬЗЯ — {supplyDeficits.Count} материал(ов) физически не хватает потребителям, и разрыв не " +
                "закрывается ни доньмом, ни новыми фабриками (см. §1d). " +
                "Это блокирует раньше окупаемости (§1b) намеренно: окупаемость считается при загрузке 100%, а " +
                "недокормленный уровень работает на долю u от мощности — его окупаемость растягивается в 1/u раз, " +
                "оставаясь формально зелёной, зато содержание и зарплату он платит полностью каждый ход " +
                "(проверено 2026-09-07, docs/economy-accounting-audit.md). Чинить ProductionRate/количества входов, " +
                "дальше можно не смотреть.");
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

        if (badSteadyStates.Count > 0)
        {
            // §1c намеренно пессимистичен — предполагает, что потолок вложений в поколение/R&D
            // платится КАЖДЫЙ ход партии, хотя по факту (см. doc-comment IdealHallCalculator и
            // GenerationResearchStep) инвестиции прекращаются, как только достигнут порог: «не
            // списывается». Если разблокировка происходит рано относительно длины партии, реальный
            // (динамический, по ходам) идеальный зал §3 может быть здоровым, даже когда статический
            // §1c формально не сходится — найдено 2026-08-23 на синтетической цепочке (ускорили
            // ResearchPointThresholdsByGeneration, X(90) ушёл из -9317 в +8770, а §1c остался ❌).
            // Блокируем только те секторы, где ОБЕ проверки согласны, что дело плохо — иначе §1c
            // ложно останавливает вердикт до того, как он успевает увидеть, что дальше всё хорошо.
            var stillFailing = badSteadyStates
                .Where(s => !idealVerdicts.TryGetValue(s.SectorId, out var v) || (!v.IsPositive && !v.IsRecovering))
                .ToList();
            var reconciledByIdealHall = badSteadyStates.Except(stillFailing).ToList();

            if (stillFailing.Count > 0)
            {
                var sectorIds = string.Join(", ", stillFailing.Select(s => s.SectorId));
                Console.WriteLine(
                    $"❌ ИГРАТЬ НЕЛЬЗЯ — сектор(а) {sectorIds} не сводят концы с концами даже в устойчивом состоянии " +
                    "(см. §1c), и идеальный зал (§3) по ним тоже не восстанавливается — вся цепочка уже построена, а " +
                    "зарплата + вложения в поколение/R&D по потолку всё равно превышают суммарную прибыль. " +
                    "Окупаемость уровней (§1b) необходима, но не достаточна — нужно либо больше прибыли " +
                    "(глубже/шире цепочка), либо меньше рабочих/темп вложений, дальше можно не смотреть.");
                return;
            }

            if (reconciledByIdealHall.Count > 0)
            {
                var sectorIds = string.Join(", ", reconciledByIdealHall.Select(s => s.SectorId));
                Console.WriteLine(
                    $"⚠ §1c формально не сходится у сектора(ов) {sectorIds}, но идеальный зал (§3) по ним всё же " +
                    "здоров — известное ограничение §1c (пессимистично считает потолок вложений вечным, не " +
                    "учитывает, что инвестиции прекращаются после разблокировки). Не блокирует вердикт, смотрите §3.");
            }
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
