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
        // Заголовок группы одинаковых поводов (блок 2 редизайна) подчиняется тому же правилу.
        var text = $"{headline} {detail} {DashboardDisplay.AttentionGroupHeadline(item, 3)}".ToLowerInvariant();

        string[] advice =
        [
            "постройте", "купите", "продайте", "наймите", "закажите", "вложите", "уменьшите",
            "увеличьте", "рекомендуем", "стоит ", "лучше ", "нужно ",
        ];
        Assert.All(advice, word => Assert.DoesNotContain(word, text));
    }

    /// <summary>
    /// Короткая причина в строке фабрики «Производства» подчиняется тому же правилу, что и панель, и
    /// не повторяет имя фабрики — оно уже стоит в строке.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void The_Factory_Row_Reason_Is_A_Fact_Without_The_Factory_Name(TeamAttentionCalculator.AttentionItem item)
    {
        if (DashboardDisplay.AttentionFactoryReason(item, MillId, Naming) is not { } reason)
        {
            // Повод не про фабрику — склад, поставка; в строке фабрики ему не место.
            Assert.Null(TeamScreen.AttentionTargetFactoryId(item));
            return;
        }

        Assert.DoesNotContain("Сталелитейный завод", reason);
        string[] advice = ["постройте", "купите", "продайте", "наймите", "закажите", "вложите", "нужно "];
        Assert.All(advice, word => Assert.DoesNotContain(word, reason.ToLowerInvariant()));
    }

    [Fact]
    public void The_Factory_Row_Reason_Is_Only_Shown_On_The_Factory_It_Is_About()
    {
        var item = new TeamAttentionCalculator.AttentionItem.FactoryWithoutWorkers(MillId);

        Assert.Equal("нет рабочих", DashboardDisplay.AttentionFactoryReason(item, MillId, Naming));
        Assert.Null(DashboardDisplay.AttentionFactoryReason(item, Ulid.NewUlid(), Naming));
    }

    [Fact]
    public void Same_Kind_Items_Get_One_Group_Headline_With_A_Count()
    {
        var headline = DashboardDisplay.AttentionGroupHeadline(
            new TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive(MillId, 1, "a", "b"), 3);

        Assert.Equal("Капремонт подорожает: 3 фабрики", headline);
    }

    [Fact]
    public void An_Overhaul_Warning_Leads_To_The_Wear_Tab_Not_To_Workers()
    {
        // Раньше «Открыть фабрику» всегда вело на «Люди» — даже для предупреждения про капремонт.
        Assert.Equal("wear", DashboardDisplay.AttentionFactoryTab(
            new TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive(MillId, 1, "a", "b")));
        Assert.Equal("workers", DashboardDisplay.AttentionFactoryTab(
            new TeamAttentionCalculator.AttentionItem.FactoryWithoutWorkers(MillId)));
    }

    [Theory]
    [InlineData(1, "1 фабрика")]
    [InlineData(3, "3 фабрики")]
    [InlineData(5, "5 фабрик")]
    [InlineData(12, "12 фабрик")]
    [InlineData(22, "22 фабрики")]
    public void Factory_Count_Is_Declined(int count, string expected) =>
        Assert.Equal(expected, DashboardDisplay.Factories(count));

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
