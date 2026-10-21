using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Game.Config;
using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Config.News;
using Game.Config.ProductionModel;
using Game.Config.Session;
using Game.Engine;

namespace Game.Bots.Llm.Tests;

/// <summary>
/// Мультикомандный прогон файловых ботов (три сектора, одна сессия): тик один на всех, обмен
/// по трём каталогам, межкомандная доска заявок через уже существующий <c>BotCommandExecutor</c>.
/// Отвечающий эмулируется так же, как exchange.py делает это в бою: ответ + SHA-256 в approved.
/// Ядро прогона (<see cref="FileBotMultiExperiment.RunCoreAsync"/>) вызывается с фикстурой
/// tiny-3-sectors и inline-сессионными параметрами — файл настройки сессии в фикстурах не копируется.
/// </summary>
public sealed class FileBotMultiTests
{
    private static readonly string[] SectorIds = ["sector1", "sector2", "sector3"];

    /// <summary>tiny-3-sectors + сжатая сессия на 12 ходов, cost-plus (регрессионный режим цен — точные дельты баланса), аварийные закупки включены.</summary>
    private static ResolvedGameConfig Config()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-3-sectors.json");
        var model = JsonSerializer.Deserialize<ProductionModelConfig>(File.ReadAllText(modelPath))
            ?? throw new JsonException("tiny-3-sectors fixture deserialized to null.");
        var session = new SessionConfig
        {
            StartingConditions = new StartingConditionsConfig { MaxInitialBuildBudget = 1_000_000m },
            Duration = new SessionDurationConfig { MinTurns = 12, MaxTurns = 12 },
            PhaseTiming = new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
            Economy = new SessionEconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 2m,
                EmergencyPurchasePressureMultiplierPerUnit = 0m,
                EmergencyPurchasePressureHalfLifeTurns = 3,
                MarketCapacityOverflowDiscount = 0.5m,
                ElectricityBasePrice = 1m,
                ElectricityConsumptionPerOutputUnit = 0m,
                TrendScenario = [],
                WarehouseLiquidationRate = 0.5m,
            },
            WorkerProductivity = new WorkerProductivityConfig
            {
                BaseWorkerCount = 5,
                DiminishingReturnsFactor = 0.5m,
                HireCostPerWorker = 50m, MaxHiresPerTurn = 1000,
                FireCostPerWorker = 30m,
                SalaryPerWorkerPerTurn = 5m,
            },
            Rnd = new RndConfig
            {
                ResearchPointThresholdsByLevel = [100m, 300m],
                DiminishingReturnsExponent = 1m,
                ProductionRateBonusPerLevel = 0.1m,
                MaxCommitmentPerTurn = 200m,
            },
            Wear = new WearConfig
            {
                GracePeriodTurns = 1000,
                BaseWearRatePerTurn = 0.01m,
                AccelerationFactorPerTurn = 0.004m,
                MaxUpkeepPenaltyMultiplier = 0.5m,
                OverhaulTiers = [new OverhaulTierConfig { Id = "prevention", Name = "Профилактика", MinCondition = 0.9m, CostFraction = 0.02m, DurationTurns = 1, OutputMultiplier = 0.97m, SalaryRate = 1m, UpkeepRate = 1m }],
                CriticalConditionThreshold = 0.2m,
                ForcedRepairDurationTurns = 8,
                ForcedRepairSalaryRate = 0.66m,
                ForcedRepairUpkeepRate = 0.5m,
                PostForcedRepairCondition = 0.85m,
            },
            Warehouse = new WarehouseConfig { FreeCapacity = 1_000_000m, OverageFeePerUnit = 0.1m },
            Reputation = new ReputationConfig { HalfLifeTurns = 10, WarmupTurns = 3, TerminationSeverityMultiplier = 3m },
            Contracts = new ContractsConfig
            {
                DeliveryMissPenaltyRate = 0.1m,
                TerminationPenaltyRate = 0.5m,
                VoluntaryTerminationFee = 100m,
                MaxActiveContractsPerTeam = null,
            },
            News = [],
            FeatureFlags = new FeatureFlagsConfig { EmergencyPurchaseEnabled = true },
        };
        return GameConfigLoader.Load(model, session);
    }

    /// <summary>delegate: собрать ответ команды для хода; запрос прилагается разобранным.</summary>
    private delegate string Respond(int teamIndex, int turn, JsonDocument request);

    /// <summary>Пин: стандартная обвязка ответа exchange.py — requestId + actions.</summary>
    private static string Response(int turn, params object[] actions) =>
        JsonSerializer.Serialize(new { requestId = $"turn-{turn}", actions });

    /// <summary>Пин: команда строит сырьевую фабрику своего сектора и нанимает 20 рабочих (raw instant — первый производственный ход следующий). Суффикс id в фикс­туре tiny-3-sectors равен номеру сектора (1..3), не «sectorN».</summary>
    private static string BuildRawAndHire(int teamIndex, int turn) =>
        Response(turn,
            new { kind = "buildFactory", factoryDefinitionId = $"factory1_{teamIndex + 1}" },
            new { kind = "setWorkerCount", factory = $"new:factory1_{teamIndex + 1}", count = 20 });

    private static FileBotMultiExperiment.Settings Settings(string baseDir, int turnLimit = 2) => new()
    {
        Directory = baseDir,
        ProductionModel = "tiny-3-sectors.json", // читается только RunAsync; RunCoreAsync берёт конфиг от теста
        TurnLimit = turnLimit,
        Seed = 42,
        MaxActions = 12,
        WaitSeconds = 30,
        Teams =
        [
            new FileBotMultiExperiment.Settings.TeamSettings { Name = "Alpha", Directory = "team1", SectorId = SectorIds[0], Rules = "Alpha persona: start its own raw chain." },
            new FileBotMultiExperiment.Settings.TeamSettings { Name = "Beta", Directory = "team2", SectorId = SectorIds[1], Rules = "Beta persona: start its own raw chain." },
            new FileBotMultiExperiment.Settings.TeamSettings { Name = "Gamma", Directory = "team3", SectorId = SectorIds[2], Rules = "Gamma persona: start its own raw chain." },
        ],
    };

    /// <summary>Эмуляция exchange.py: пока прогона нет result.json и не велено прерваться — читает каждый 000-й request, пишет response + approved (SHA-256).</summary>
    private static async Task ServeAsync(FileBotMultiExperiment.Settings settings, Respond respond, Func<bool>? abort = null)
    {
        var basePath = Path.GetFullPath(settings.Directory);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        var served = new HashSet<string>();
        while (true)
        {
            if (abort is not null && abort()) return;
            if (File.Exists(Path.Combine(basePath, "result.json"))) return;
            if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException("Serve loop hit the deadline without result.json.");
            var acted = false;
            for (var i = 0; i < settings.Teams.Count; i++)
            {
                var dir = Path.GetFullPath(Path.Combine(basePath, settings.Teams[i].Directory));
                if (!Directory.Exists(dir)) continue;
                foreach (var requestPath in Directory.EnumerateFiles(dir, "*.request.json"))
                {
                    var key = $"{i}|{requestPath}";
                    if (served.Contains(key)) continue;
                    if (!TryReadJson(requestPath, out var request)) continue;
                    served.Add(key);
                    var stem = Path.GetFileName(requestPath).Replace(".request.json", "");
                    var response = respond(i, int.Parse(stem), request);
                    await File.WriteAllTextAsync(requestPath.Replace(".request.json", ".response.json"), response);
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response)));
                    await File.WriteAllTextAsync(requestPath.Replace(".request.json", ".approved"), hash);
                    acted = true;
                }
            }
            if (!acted) await Task.Delay(20);
        }
    }

    /// <summary>Файл может быть прочитан в момент записи раннером — до полного JSON дожидаемся циклом, но не помечаем «ответанным».</summary>
    private static bool TryReadJson(string path, out JsonDocument document)
    {
        document = default!;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<JsonDocument> RunAsync(ResolvedGameConfig config, FileBotMultiExperiment.Settings settings, Respond respond, Func<bool>? abort = null)
    {
        var run = FileBotMultiExperiment.RunCoreAsync(settings, config);
        await ServeAsync(settings, respond, abort);
        await run;
        return JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(settings.Directory, "result.json")));
    }

    private static string TempDir(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fbot-multi-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task ThreeTeamsTwoTurns_TkOneForAll_SectorsCatalogsFiltered_ResultHonest()
    {
        var config = Config();
        var dir = TempDir("three");
        var settings = Settings(dir);
        var result = await RunAsync(config, settings, (i, turn, _) => turn == 1
            ? BuildRawAndHire(i, turn)
            : Response(turn, new { kind = "sellToSystem", materialId = $"material1_{i + 1}", volume = 100m }));

        Assert.Equal("turn-limit", result.RootElement.GetProperty("stopReason").GetString());
        Assert.Equal(2, result.RootElement.GetProperty("completedTurns").GetInt32());
        Assert.True(result.RootElement.GetProperty("journalIntegrity").GetBoolean());
        var teams = result.RootElement.GetProperty("teamResults");
        Assert.Equal(3, teams.GetArrayLength());
        Assert.All(teams.EnumerateArray(), t => Assert.Equal(1, t.GetProperty("factories").GetInt32()));

        // Первый запрос T1: свой rules-текст, общий протокол, каталог ТОЛЬКО своего сектора,
        // рынок и доска — на все сектора, обзор команд, пустые предыдущие результаты.
        var first = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(dir, "team1", "001.request.json")));
        Assert.Equal("turn-1", first.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(3, first.RootElement.GetProperty("teamsOverview").GetArrayLength());
        Assert.Contains("fulfillTradeOffer", first.RootElement.GetProperty("protocol").GetString());
        Assert.Equal("Alpha persona: start its own raw chain.", first.RootElement.GetProperty("rules").GetString());
        Assert.Equal(0, first.RootElement.GetProperty("tradeOffers").GetArrayLength());
        Assert.Equal(0, first.RootElement.GetProperty("needs").GetArrayLength());
        var firstCatalogueJson = first.RootElement.GetProperty("catalogue").GetRawText();
        Assert.Contains("factory2_1", firstCatalogueJson); // свой сектор: весь вид
        Assert.DoesNotContain("factory2_2", firstCatalogueJson); // чужой сектор отфильтрован
        var firstMarketJson = first.RootElement.GetProperty("market").GetRawText();
        Assert.Contains("material2_2", firstMarketJson); // рынок — на все сектора

        // Второй запрос T2: rules на продолжении уже не повторяется, предыдущие результаты свои.
        var second = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(dir, "team2", "002.request.json")));
        Assert.False(second.RootElement.TryGetProperty("rules", out _));
        Assert.False(second.RootElement.TryGetProperty("protocol", out _));
        var secondPrevious = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(dir, "team1", "002.request.json")));
        Assert.True(secondPrevious.RootElement.GetProperty("previousResults").GetArrayLength() > 0);
        // Метрики — по одному файлу на команду, по строке на ход; на ходу 2 команды действительно
        // продали свой сырьевой материал системе (результаты ок, а не ошибки протокола).
        foreach (var teamDir in new[] { "team1", "team2", "team3" })
        {
            var lines = await File.ReadAllLinesAsync(Path.Combine(dir, teamDir, "metrics.jsonl"));
            Assert.Equal(2, lines.Length);
            var last = JsonDocument.Parse(lines[1]).RootElement.GetProperty("results");
            Assert.Contains(last.EnumerateArray(), r => (r.GetString() ?? "").StartsWith("ok:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task CrossSectorBuyOfferPostedTurn1_FulfilledTurn2_MaterialArrivesInBuyerStock()
    {
        var config = Config();
        var dir = TempDir("cross");
        var settings = Settings(dir, turnLimit: 3);
        string? offerId = null;
        var result = await RunAsync(config, settings, (i, turn, request) =>
        {
            if (turn == 1)
            {
                return i switch
                {
                    0 => Response(turn, new { kind = "postBuyOffer", materialId = "material1_2", volume = 3m, minPrice = 5m, maxPrice = 25m }),
                    1 => BuildRawAndHire(i, turn),
                    _ => Response(turn),
                };
            }
            if (turn == 2 && i == 1)
            {
                // Доска в запросе T2 этого хода содержит заявку T1 с хода 1 — идентификатор оттуда.
                offerId ??= request.RootElement.GetProperty("tradeOffers").EnumerateArray().Single()[0].GetString()
                    ?? throw new Xunit.Sdk.XunitException("Board offer id must be present.");
                return Response(turn, new { kind = "fulfillTradeOffer", tradeOfferId = offerId, volume = 3m, unitPrice = 8m });
            }
            return Response(turn);
        });

        Assert.Equal("turn-limit", result.RootElement.GetProperty("stopReason").GetString());
        Assert.Equal(3, result.RootElement.GetProperty("completedTurns").GetInt32());
        Assert.True(result.RootElement.GetProperty("journalIntegrity").GetBoolean());
        Assert.NotNull(offerId);

        // Заявка была на доске хода 2 в запросе T2, доска хода 3 пуста (заявка исполнена).
        var board2 = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(dir, "team3", "002.request.json")));
        var offers2 = board2.RootElement.GetProperty("tradeOffers");
        var offer = offers2.EnumerateArray().Single();
        Assert.Equal(offerId, offer[0].GetString());
        Assert.Equal("T1", offer[1].GetString());
        Assert.Equal("buy", offer[2].GetString());
        Assert.Equal("material1_2", offer[3].GetString());
        Assert.Equal("spot", offer[4].GetString());
        Assert.Equal(3m, offer[5].GetDecimal());
        var board3 = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(dir, "team3", "003.request.json")));
        Assert.Equal(0, board3.RootElement.GetProperty("tradeOffers").GetArrayLength());

        // Контрактный платёж: T1 платит только 3×8=24 за поставку; spot на ход 3 приходит в склад.
        var t1 = result.RootElement.GetProperty("teamResults").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Alpha");
        Assert.Equal(-24m, t1.GetProperty("balance").GetDecimal());
        var stock3 = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(dir, "team1", "003.request.json")));
        Assert.Contains("material1_2", stock3.RootElement.GetProperty("stock").GetRawText());
    }

    [Fact]
    public async Task EmergencyPurchase_BuysForeignIntermediate_AtCostPlusMultiplier_NextTick()
    {
        var config = Config();
        var dir = TempDir("emergency");
        var settings = Settings(dir);
        var firstBalances = new decimal[3];
        var result = await RunAsync(config, settings, (i, turn, request) =>
        {
            if (turn == 1)
            {
                firstBalances[i] = request.RootElement.GetProperty("balance").GetDecimal();
                return i == 0
                    ? Response(turn, new { kind = "emergencyPurchase", materialId = "material1_3", volume = 2m })
                    : Response(turn);
            }
            return Response(turn);
        });

        Assert.Equal("turn-limit", result.RootElement.GetProperty("stopReason").GetString());
        Assert.True(result.RootElement.GetProperty("journalIntegrity").GetBoolean());

        // Закупка исполнена на следующем расчёте: material1_3 (чужого сектора) в складе T1 на ход 2,
        // баланс ушёл ровно на TotalCost события EmergencyPurchased (в журнале), объём прибыл полностью.
        var stock2 = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(dir, "team1", "002.request.json")));
        var stockJson = stock2.RootElement.GetProperty("stock").GetRawText();
        Assert.Contains("material1_3", stockJson);
        var secondBalance = stock2.RootElement.GetProperty("balance").GetDecimal();
        var purchasedTotal = 0m;
        foreach (var line in await File.ReadAllLinesAsync(Path.Combine(dir, "journal.jsonl")))
        {
            if (!line.Contains("EmergencyPurchased")) continue;
            var entry = JsonDocument.Parse(line);
            var change = JsonDocument.Parse(entry.RootElement.GetProperty("ChangeJson").GetString()!);
            Assert.Equal(2m, change.RootElement.GetProperty("Volume").GetDecimal());
            purchasedTotal += change.RootElement.GetProperty("TotalCost").GetDecimal();
        }
        Assert.Equal(decimal.Round(firstBalances[0] - purchasedTotal, 2), decimal.Round(secondBalance, 2));
    }

    [Fact]
    public async Task StopFileInAnyTeamDirectory_StopsWholeRun_WithHonestPartialResult()
    {
        var config = Config();
        var dir = TempDir("stop");
        var settings = Settings(dir, turnLimit: 4);
        var team1Turn2Request = Path.Combine(dir, "team1", "002.request.json");
        // Прерываемся, как только раннер напишет запросы хода 2: STOP в каталоге T2, ход 2 никем не одобрен.
        Func<bool>? abort = () =>
        {
            if (File.Exists(team1Turn2Request))
            {
                File.WriteAllText(Path.Combine(dir, "team2", "STOP"), "test stop");
                return true;
            }
            return false;
        };
        var result = await RunAsync(config, settings, (i, turn, _) => turn == 1 ? BuildRawAndHire(i, turn) : Response(turn), abort);

        Assert.Equal("stopped:stop-file:2", result.RootElement.GetProperty("stopReason").GetString());
        Assert.Equal(1, result.RootElement.GetProperty("completedTurns").GetInt32());
        Assert.True(result.RootElement.GetProperty("journalIntegrity").GetBoolean());
        Assert.Equal(3, result.RootElement.GetProperty("teamResults").GetArrayLength());
        foreach (var teamDir in new[] { "team1", "team2", "team3" })
        {
            var lines = await File.ReadAllLinesAsync(Path.Combine(dir, teamDir, "metrics.jsonl"));
            Assert.Single(lines);
            Assert.True(File.Exists(Path.Combine(dir, teamDir, "002.request.json"))); // запрос написан, одобрений нет
            Assert.False(File.Exists(Path.Combine(dir, teamDir, "002.approved")));
        }
    }

    [Fact]
    public async Task Determinism_SameSeedSameScenario_SameRequestsAndFinalBalances()
    {
        var config = Config();
        var dirA = TempDir("detA");
        var dirB = TempDir("detB");
        var settingsA = Settings(dirA);
        var settingsB = Settings(dirB);
        Respond respond = (i, turn, _) => turn == 1
            ? BuildRawAndHire(i, turn)
            : Response(turn, new { kind = "sellToSystem", materialId = $"material1_{SectorIds[i]}", volume = 30m });
        var resultA = await RunAsync(config, settingsA, respond);
        var resultB = await RunAsync(config, settingsB, respond);

        for (var i = 0; i < 3; i++)
        {
            var firstA = await File.ReadAllTextAsync(Path.Combine(dirA, $"team{i + 1}", "001.request.json"));
            var firstB = await File.ReadAllTextAsync(Path.Combine(dirB, $"team{i + 1}", "001.request.json"));
            Assert.Equal(firstA, firstB);
        }
        var balancesA = resultA.RootElement.GetProperty("teamResults").EnumerateArray().Select(t => t.GetProperty("balance").GetDecimal()).ToArray();
        var balancesB = resultB.RootElement.GetProperty("teamResults").EnumerateArray().Select(t => t.GetProperty("balance").GetDecimal()).ToArray();
        Assert.Equal(balancesA, balancesB);
    }
}
