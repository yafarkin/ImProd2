using Game.Domain;

namespace Game.Web;

/// <summary>
/// Цвет сектора для SVG-диаграмм (<see cref="MaterialChainDiagram"/>) — общая палитра и правило
/// присвоения, чтобы один и тот же сектор был одного и того же цвета везде. Та же палитра
/// (<see cref="Palette"/>) переиспользуется и для категориальных серий графиков
/// (<see cref="LineChartDiagram"/>) — она уже проверена на разделимость, заново генерировать цвета
/// под каждый новый график не нужно.
/// </summary>
public static class SectorColors
{
    // Категориальная палитра (dataviz skill, references/palette.md) — фиксированный порядок,
    // проверенный validate_palette.js на CVD-разделимость и контраст относительно белого холста;
    // не переставлять и не генерировать динамически. Секторам присваивается по порядку их
    // перечисления в конфиге.
    internal static readonly string[] Palette =
    [
        "#2a78d6", "#008300", "#e87ba4", "#eda100", "#1baf7a", "#eb6834", "#4a3aa7", "#e34948",
    ];

    /// <summary>Цвет для каждого сектора конфига, по порядку его перечисления.</summary>
    public static IReadOnlyDictionary<string, string> ForSectors(IReadOnlyList<Sector> sectors)
    {
        ArgumentNullException.ThrowIfNull(sectors);

        return sectors
            .Select((sector, index) => (sector.Id, Color: Palette[index % Palette.Length]))
            .ToDictionary(entry => entry.Id, entry => entry.Color);
    }
}
