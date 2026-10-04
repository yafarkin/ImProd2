using Game.Domain;

namespace Game.Web;

/// <summary>
/// Доска потребностей зала, сгруппированная по материалу (docs/manager-ui/README.md §4, блок 4, макет
/// mockups/needs.html). Раньше это была таблица «одна запись — одна строка», и чтобы увидеть, что по
/// одному товару есть и спрос, и предложение, приходилось глазами сводить строки разных команд.
/// <para>
/// Пометка «у нас N» — факт со склада, а не совет продавать. Порядок: сначала материалы, которые
/// касаются нас (есть у нас, нужны нам или в них есть наша запись), потом остальные по алфавиту.
/// </para>
/// </summary>
public static class NeedsBoard
{
    /// <summary>Одна запись доски. <see cref="IsOurs"/> — её опубликовала наша команда.</summary>
    public sealed record Entry(
        Ulid NeedId,
        string TeamName,
        bool IsOurs,
        NeedDirection Direction,
        NeedVolumeOrder VolumeOrder,
        string? Comment);

    /// <summary>
    /// Все записи по одному материалу. <see cref="OurStock"/> — сколько его у нас на складе (0 — нет);
    /// <see cref="NeededByUs"/> — материал нужен нашим фабрикам как сырьё; <see cref="ConcernsUs"/> —
    /// материал касается нас хоть как-то, по нему работает фильтр «По нашим материалам».
    /// </summary>
    public sealed record MaterialGroup(
        Material Material,
        decimal OurStock,
        bool NeededByUs,
        IReadOnlyList<Entry> Entries)
    {
        public bool ConcernsUs => OurStock > 0 || NeededByUs || Entries.Any(entry => entry.IsOurs);
    }

    /// <summary>
    /// <paramref name="activeNeeds"/> — действующие записи всех команд; <paramref name="teamNames"/> —
    /// имена команд; <paramref name="ourStock"/> — остатки нашего склада по материалам;
    /// <paramref name="neededByUs"/> — материалы, которые наши фабрики берут на вход.
    /// </summary>
    public static IReadOnlyList<MaterialGroup> Build(
        IReadOnlyList<NeedPosting> activeNeeds,
        IReadOnlyDictionary<Ulid, string> teamNames,
        Ulid ourTeamId,
        IReadOnlyDictionary<string, decimal> ourStock,
        IReadOnlySet<string> neededByUs)
    {
        ArgumentNullException.ThrowIfNull(activeNeeds);
        ArgumentNullException.ThrowIfNull(teamNames);
        ArgumentNullException.ThrowIfNull(ourStock);
        ArgumentNullException.ThrowIfNull(neededByUs);

        return activeNeeds
            .GroupBy(need => need.Material.Id)
            .Select(group =>
            {
                var material = group.First().Material;
                var entries = group
                    // Внутри материала: сначала наши записи, потом чужие по имени команды; при равенстве —
                    // по Id, чтобы порядок не зависел от порядка словаря в состоянии.
                    .OrderByDescending(need => need.TeamId == ourTeamId)
                    .ThenBy(need => teamNames.GetValueOrDefault(need.TeamId, "?"), StringComparer.Ordinal)
                    .ThenBy(need => need.Id)
                    .Select(need => new Entry(
                        need.Id,
                        teamNames.GetValueOrDefault(need.TeamId, "?"),
                        need.TeamId == ourTeamId,
                        need.Direction,
                        need.VolumeOrder,
                        string.IsNullOrWhiteSpace(need.Comment) ? null : need.Comment))
                    .ToList();
                return new MaterialGroup(material, ourStock.GetValueOrDefault(material.Id), neededByUs.Contains(material.Id), entries);
            })
            .OrderByDescending(group => group.ConcernsUs)
            .ThenBy(group => group.Material.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Глагол записи: «ищет» / «предлагает», для своей команды — «ищем» / «предлагаем».</summary>
    public static string DirectionVerb(NeedDirection direction, bool ours) => (direction, ours) switch
    {
        (NeedDirection.Deficit, false) => "ищет",
        (NeedDirection.Deficit, true) => "ищем",
        (NeedDirection.Surplus, false) => "предлагает",
        _ => "предлагаем",
    };
}
