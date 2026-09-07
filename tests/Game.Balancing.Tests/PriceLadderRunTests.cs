using System.Text.Json;
using Game.Config.Economy;

namespace Game.Balancing.Tests;

/// <summary>
/// Запись лестницы цен обратно в файл конфига (<c>--mode price-ladder --apply</c>, блок 11.2) —
/// самая рискованная часть режима: она правит боевые файлы моделей на месте.
///
/// <para>Проверяется главным образом <b>точечность</b> правки. Пересборка файла из
/// <c>GameConfig</c> переформатировала бы модель целиком (в <c>main-3-sectors.json</c> — под тысячу
/// строк), и настоящая правка цен утонула бы в диффе, где её невозможно проверить глазами; поэтому
/// запись сделана точечной, и тесты стерегут именно это свойство.</para>
/// </summary>
public class PriceLadderRunTests
{
    private static IReadOnlyList<SystemSalePriceLadderCalculator.MaterialLadderRow> Rows() =>
    [
        new SystemSalePriceLadderCalculator.MaterialLadderRow("ore", "Руда", "A", 0, UnitCost: 2m, OldPrice: 1m, NewPrice: 2.6m),
        new SystemSalePriceLadderCalculator.MaterialLadderRow("sheet", "Лист", "A", 1, UnitCost: 10m, OldPrice: 1m, NewPrice: 14m),
    ];

    private static string WriteTempFile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"price-ladder-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Файл production-модели: массив цен лежит в корне.</summary>
    [Fact]
    public void Prices_Are_Written_Into_A_Production_Model_File()
    {
        var path = WriteTempFile(
            """
            {
              "Sectors": [ { "Id": "A", "Name": "Металлургия" } ],
              "BaseMarketPerMaterial": [
                { "MaterialId": "ore", "BasePrice": 1, "BaseCapacity": 100 },
                { "MaterialId": "sheet", "BasePrice": 1, "BaseCapacity": 10 }
              ]
            }
            """);

        try
        {
            var updated = PriceLadderRun.ApplyToFile(path, Rows());

            Assert.Equal(2, updated);
            var market = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("BaseMarketPerMaterial");
            Assert.Equal(2.6m, market[0].GetProperty("BasePrice").GetDecimal());
            Assert.Equal(14m, market[1].GetProperty("BasePrice").GetDecimal());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Уже собранный <c>GameConfig</c>: тот же массив лежит внутри <c>Economy</c>.</summary>
    [Fact]
    public void Prices_Are_Written_Into_A_Fully_Composed_Game_Config_File()
    {
        var path = WriteTempFile(
            """
            {
              "Duration": { "MinTurns": 80, "MaxTurns": 90 },
              "Economy": {
                "ElectricityBasePrice": 2.0,
                "BaseMarketPerMaterial": [
                  { "MaterialId": "ore", "BasePrice": 1, "BaseCapacity": 100 }
                ]
              }
            }
            """);

        try
        {
            var updated = PriceLadderRun.ApplyToFile(path, Rows());

            Assert.Equal(1, updated);
            var economy = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Economy");
            Assert.Equal(2.6m, economy.GetProperty("BaseMarketPerMaterial")[0].GetProperty("BasePrice").GetDecimal());
            Assert.Equal(2.0m, economy.GetProperty("ElectricityBasePrice").GetDecimal());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Всё, кроме цен, остаётся на месте — включая ёмкость рынка в тех же записях, которые правятся.
    /// Ёмкость откалибрована отдельно и к цене отношения не имеет.
    /// </summary>
    [Fact]
    public void Nothing_But_The_Prices_Changes_In_The_File()
    {
        var path = WriteTempFile(
            """
            {
              "Sectors": [ { "Id": "A", "Name": "Металлургия" } ],
              "Recipes": [ { "Id": "mining", "ProductionRate": 1000 } ],
              "BaseMarketPerMaterial": [
                { "MaterialId": "ore", "BasePrice": 1, "BaseCapacity": 100 }
              ],
              "GenerationResearch": { "Thresholds": [ 1, 2, 3 ] }
            }
            """);

        try
        {
            PriceLadderRun.ApplyToFile(path, Rows());

            var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            Assert.Equal("Металлургия", root.GetProperty("Sectors")[0].GetProperty("Name").GetString());
            Assert.Equal(1000, root.GetProperty("Recipes")[0].GetProperty("ProductionRate").GetInt32());
            Assert.Equal(100, root.GetProperty("BaseMarketPerMaterial")[0].GetProperty("BaseCapacity").GetInt32());
            Assert.Equal(3, root.GetProperty("GenerationResearch").GetProperty("Thresholds").GetArrayLength());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Материал, которого нет в лестнице, не трогается — и попадает в расхождение счётчиков, о
    /// котором режим предупреждает вслух («--config указывает не на ту модель»).
    /// </summary>
    [Fact]
    public void A_Material_Missing_From_The_Ladder_Keeps_Its_Old_Price_And_Is_Counted_As_Not_Updated()
    {
        var path = WriteTempFile(
            """
            {
              "BaseMarketPerMaterial": [
                { "MaterialId": "ore", "BasePrice": 1, "BaseCapacity": 100 },
                { "MaterialId": "unrelated", "BasePrice": 7, "BaseCapacity": 10 }
              ]
            }
            """);

        try
        {
            var updated = PriceLadderRun.ApplyToFile(path, Rows());

            Assert.Equal(1, updated);
            Assert.NotEqual(Rows().Count, updated);
            var market = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("BaseMarketPerMaterial");
            Assert.Equal(7m, market[1].GetProperty("BasePrice").GetDecimal());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Файл без блока рыночных цен — ошибка с внятным текстом, а не тихо ничего не сделавший прогон.</summary>
    [Fact]
    public void A_File_Without_A_Market_Block_Is_Reported_Loudly()
    {
        var path = WriteTempFile("""{ "Sectors": [] }""");

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => PriceLadderRun.ApplyToFile(path, Rows()));
            Assert.Contains("BaseMarketPerMaterial", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
