using Game.Engine;

namespace Game.Web.Tests;

/// <summary>
/// Подписи панели «Требует внимания» (docs/TODO.md №7). Расчёт живёт в движке и оперирует
/// идентификаторами (<see cref="TeamAttentionCalculator"/>), а текст для человека собирается здесь —
/// тот же приём, что у подписей финансовых операций.
///
/// <para>
/// Отдельно проверяется главное свойство формулировок: они описывают, ЧТО происходит и ПОЧЕМУ, но
/// не советуют, что делать. Панель адресована тем, кто играет первый раз, и обязана спасти их от
/// провала на основах, а не сыграть партию за них — тест на отсутствие повелительного наклонения
/// сторожит ровно эту границу, которую легко размыть одной «доброй» правкой текста.
/// </para>
/// </summary>
public class AttentionTextTests
{
    private static readonly Ulid MillId = Ulid.NewUlid();
    private static readonly Ulid ContractId = Ulid.NewUlid();

    private static readonly DashboardDisplay.AttentionNaming Naming = new(
        new Dictionary<Ulid, string> { [MillId] = "Сталелитейный завод" },
        new Dictionary<string, string> { ["ore"] = "Руда" },
        new Dictionary<string, string> { ["prevention"] = "Профилактика", ["scheduled"] = "Плановое обслуживание" });

    public static TheoryData<TeamAttentionCalculator.AttentionItem> AllKinds() =>
    [
        new TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput(MillId, "ore", 3, 10m),
        new TeamAttentionCalculator.AttentionItem.FactoryWithoutWorkers(MillId),
        new TeamAttentionCalculator.AttentionItem.FactoryInForcedDowntime(MillId, 5),
        new TeamAttentionCalculator.AttentionItem.DeliveryDueAndShort(ContractId, "ore", 35m, 240m),
        new TeamAttentionCalculator.AttentionItem.WarehouseOverFreeCapacity(120m, 12m),
        new TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive(MillId, 2, "prevention", "scheduled"),
        new TeamAttentionCalculator.AttentionItem.DeliveryAheadWillBeShort(ContractId, "ore", 2, 15m),
        new TeamAttentionCalculator.AttentionItem.MaterialRunningOut("ore", 3, [MillId]),
    ];

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_Kind_Has_A_Human_Readable_Headline_And_Detail(TeamAttentionCalculator.AttentionItem item)
    {
        var (headline, detail) = DashboardDisplay.AttentionText(item, Naming);

        Assert.NotEmpty(headline);
        Assert.NotEmpty(detail);
        // Голое имя типа означает, что вид повода забыли добавить в switch подписей.
        Assert.NotEqual(item.GetType().Name, headline);
    }

    /// <summary>
    /// Ни одной формы повелительного наклонения: как только в панели появится «постройте», «купите»
    /// или «продайте», она перестанет быть сводкой фактов и станет советчиком — а это прямо
    /// исключено постановкой (docs/TODO.md №7: «только очень базовые подсказки, а не решать за
    /// игрока всю игру»).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void No_Kind_Tells_The_Player_What_To_Do(TeamAttentionCalculator.AttentionItem item)
    {
        var (headline, detail) = DashboardDisplay.AttentionText(item, Naming);
        var text = $"{headline} {detail}".ToLowerInvariant();

        string[] advice =
        [
            "постройте", "купите", "продайте", "наймите", "закажите", "вложите", "уменьшите",
            "увеличьте", "рекомендуем", "стоит ", "лучше ", "нужно ",
        ];
        Assert.All(advice, word => Assert.DoesNotContain(word, text));
    }

    [Fact]
    public void Names_Come_From_The_Naming_Dictionaries()
    {
        var (headline, detail) = DashboardDisplay.AttentionText(
            new TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput(MillId, "ore", 3, 10m), Naming);

        Assert.Contains("Сталелитейный завод", headline);
        Assert.Contains("Руда", detail);
    }

    /// <summary>Идентификатор, для которого имени не нашлось, не должен ронять страницу — показываем сам идентификатор.</summary>
    [Fact]
    public void An_Unknown_Id_Falls_Back_Instead_Of_Throwing()
    {
        var (_, detail) = DashboardDisplay.AttentionText(
            new TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput(MillId, "unknown-material", 2, 1m), Naming);

        Assert.Contains("unknown-material", detail);
    }

    [Theory]
    [InlineData(1, "1 ход")]
    [InlineData(2, "2 хода")]
    [InlineData(4, "4 хода")]
    [InlineData(5, "5 ходов")]
    [InlineData(11, "11 ходов")]
    [InlineData(12, "12 ходов")]
    [InlineData(21, "21 ход")]
    [InlineData(25, "25 ходов")]
    public void Turns_Are_Declined_Correctly(int count, string expected) =>
        Assert.Equal(expected, DashboardDisplay.Turns(count));
}
