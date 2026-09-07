using System.Globalization;
using Game.Config.Economy;
using Game.Engine;

namespace Game.Web;

/// <summary>
/// Индекс деловой активности для интерфейса (блок 11.10, <c>docs/external-economy.md</c> §6) — число
/// с направлением и график истории, общие для большого экрана и дашборда команды. По тому же
/// принципу, что <see cref="DashboardDisplay"/>/<see cref="BigScreenDisplay"/>: чистые статические
/// функции над уже посчитанным состоянием, без собственного хранимого состояния.
///
/// <para><b>График показывает только прошлое.</b> Данные берутся из журнала
/// (<see cref="EconomyIndexHistoryCalculator"/>), который физически не может заглянуть вперёд;
/// прогноз в игре — работа новостной ленты, а не графика с точными числами.</para>
///
/// <para><b>Под <see cref="PricingModel.CostPlus"/> индекс не показывается вообще</b>
/// (<see cref="IsVisible"/>): он там считается и публикуется, но ни на один рубль не влияет — цена
/// выводится из себестоимости. Показать его значило бы предложить игроку строить решения на
/// величине, которая ничего не делает.</para>
/// </summary>
public static class EconomyIndexDisplay
{
    /// <summary>
    /// Текущее состояние индекса для подписи: значение, изменение к прошлому ходу, стрелка и класс
    /// цвета. <see cref="HasData"/> false — журнал ещё не содержит ни одной публикации рынка.
    /// </summary>
    public sealed record IndexSummary(
        bool HasData, decimal Index, decimal? PreviousIndex, decimal Change, string Arrow, string CssClass)
    {
        /// <summary>
        /// Значение индекса как оно показывается игроку: «1.03». Инвариантная культура — приложение
        /// запускается в globalization-invariant mode, любая именованная культура тут падает.
        /// </summary>
        public string ValueText => Index.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Отклонение от нейтрального уровня словами: «выше нейтрального на 3 %».</summary>
        public string LevelText => Index switch
        {
            _ when Index > EconomyIndexCalculator.NeutralIndex =>
                $"выше нейтрального на {(Index / EconomyIndexCalculator.NeutralIndex - 1m):P0}",
            _ when Index < EconomyIndexCalculator.NeutralIndex =>
                $"ниже нейтрального на {(1m - Index / EconomyIndexCalculator.NeutralIndex):P0}",
            _ => "на нейтральном уровне",
        };
    }

    /// <summary>Показывать ли индекс в интерфейсе вообще — см. doc-comment класса.</summary>
    public static bool IsVisible(EconomyConfig economy)
    {
        ArgumentNullException.ThrowIfNull(economy);

        return economy.PricingModel == PricingModel.External;
    }

    /// <summary>
    /// Последняя точка истории плюс направление относительно предыдущего хода. Направление —
    /// именно «ход к ходу», а не «к нейтральному уровню»: игрока интересует, куда рынок движется
    /// сейчас, а насколько он высоко — отдельная подпись (<see cref="IndexSummary.LevelText"/>).
    /// </summary>
    public static IndexSummary Describe(IReadOnlyList<EconomyIndexHistoryCalculator.IndexPoint> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (history.Count == 0)
        {
            return new IndexSummary(HasData: false, 0m, null, 0m, "→", "text-muted");
        }

        var current = history[^1].Index;
        decimal? previous = history.Count > 1 ? history[^2].Index : null;
        var change = previous is null ? 0m : current - previous.Value;

        var (arrow, cssClass) = change switch
        {
            > 0m => ("↑", "text-success"),
            < 0m => ("↓", "text-danger"),
            _ => ("→", "text-muted"),
        };

        return new IndexSummary(HasData: true, current, previous, change, arrow, cssClass);
    }

    /// <summary>
    /// График индекса по ходам. Одна линия, линейная шкала — размах индекса невелик
    /// (<see cref="EconomyConfig.EconomyIndexMin"/>..<see cref="EconomyConfig.EconomyIndexMax"/>),
    /// логарифмическая тут нечего показывать.
    /// </summary>
    public static LineChartDiagram.ChartLayout BuildChart(
        IReadOnlyList<EconomyIndexHistoryCalculator.IndexPoint> history, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(history);

        var series = new LineChartDiagram.ChartSeries(
            "Индекс деловой активности",
            SectorColors.Palette[0],
            history.Select(point => (point.Turn, point.Index)).ToList());

        return LineChartDiagram.Build(
            [series], LineChartDiagram.ChartScale.Linear, width, height,
            value => value.ToString("0.00", CultureInfo.InvariantCulture));
    }
}
