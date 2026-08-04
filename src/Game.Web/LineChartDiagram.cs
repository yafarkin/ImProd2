using System.Globalization;

namespace Game.Web;

/// <summary>
/// Раскладка линейного графика «значение по ходам» в координаты для SVG — тот же принцип, что и у
/// диаграмм фабрик (чистый статический класс раскладки + <see cref="SvgMarkup"/>), но с осями и
/// шкалой значений вместо узлов и связей. Один график — одна шкала Y (никогда две оси на одном
/// графике: разные по порядку величин ряды — это разные графики, не разные оси одного). Никакого
/// hover/тултипа — вместо него прямая подпись значения у последней точки каждого ряда (в серверном
/// Blazor без JS-интеропа интерактивность стоила бы отдельной инфраструктуры, которой в проекте нет).
/// </summary>
public static class LineChartDiagram
{
    /// <summary>Шкала оси Y. Логарифмическая — для рядов, отличающихся на порядки (руда против готового продукта).</summary>
    public enum ChartScale { Linear, Logarithmic }

    /// <summary>Один ряд на входе — подпись, цвет (фиксированный, по <see cref="SectorColors"/>-палитре, не генерируемый) и точки по ходам, уже отсортированные по возрастанию хода.</summary>
    public sealed record ChartSeries(string Label, string Color, IReadOnlyList<(int Turn, decimal Value)> Points);

    /// <summary>Один посчитанный ряд — путь для <c>&lt;path&gt;</c>, координаты последней точки и готовая подпись значения рядом с ней.</summary>
    public sealed record ChartSeriesPath(
        string Label, string Color, string PathData, double LastX, double LastY, string LastValueLabel);

    /// <summary>
    /// Итоговая раскладка графика целиком. <see cref="ZeroLineY"/> — координата нулевой отметки для
    /// отдельной жирной линии поверх обычной сетки (запрос пользователя: «чётко видно, в плюсе
    /// команда или в минусе»); заполняется только для линейной шкалы — на логарифмической ноль не
    /// имеет представимой координаты (остатки склада, которые там строятся, и так всегда ≥ 0).
    /// </summary>
    public sealed record ChartLayout(
        IReadOnlyList<ChartSeriesPath> Series,
        IReadOnlyList<(double X, double Y, string Label)> XTicks,
        IReadOnlyList<(double X, double Y, string Label)> YTicks,
        double Width, double Height,
        double? ZeroLineY = null);

    private const double LeftMargin = 56;
    private const double RightMargin = 72;
    private const double TopMargin = 16;
    private const double BottomMargin = 28;
    private const int YTickCount = 4;
    private const int MaxXTickCount = 6;

    /// <summary>
    /// <paramref name="formatValue"/> — форматирование чисел для подписей оси Y и подписи последней
    /// точки ряда (по умолчанию — просто число; для денег вызывающая сторона передаёт
    /// <see cref="DashboardDisplay.FormatMoney"/>).
    /// </summary>
    public static ChartLayout Build(
        IReadOnlyList<ChartSeries> series, ChartScale scale, double width, double height,
        Func<decimal, string>? formatValue = null)
    {
        ArgumentNullException.ThrowIfNull(series);
        formatValue ??= value => value.ToString("0.##", CultureInfo.InvariantCulture);

        var plotLeft = LeftMargin;
        var plotRight = width - RightMargin;
        var plotTop = TopMargin;
        var plotBottom = height - BottomMargin;

        var allPoints = series.SelectMany(s => s.Points).ToList();
        if (allPoints.Count == 0)
        {
            return new ChartLayout([], [], [], width, height);
        }

        var minTurn = allPoints.Min(p => p.Turn);
        var maxTurn = allPoints.Max(p => p.Turn);

        double MapX(int turn) => maxTurn == minTurn
            ? (plotLeft + plotRight) / 2
            : plotLeft + (turn - minTurn) / (double)(maxTurn - minTurn) * (plotRight - plotLeft);

        var (mapY, yTickValues) = scale == ChartScale.Logarithmic
            ? BuildLogarithmicScale(allPoints.Select(p => p.Value), plotTop, plotBottom)
            : BuildLinearScale(allPoints.Select(p => p.Value), plotTop, plotBottom);

        var seriesPaths = new List<ChartSeriesPath>();
        foreach (var s in series)
        {
            if (s.Points.Count == 0)
            {
                continue;
            }

            var ordered = s.Points.OrderBy(p => p.Turn).ToList();
            var pathData = string.Join(" ", ordered.Select((point, index) =>
            {
                var x = MapX(point.Turn);
                var y = mapY(point.Value);
                return $"{(index == 0 ? "M" : "L")}{SvgMarkup.N(x)},{SvgMarkup.N(y)}";
            }));

            var last = ordered[^1];
            seriesPaths.Add(new ChartSeriesPath(
                s.Label, s.Color, pathData, MapX(last.Turn), mapY(last.Value), formatValue(last.Value)));
        }

        var xTicks = BuildXTicks(minTurn, maxTurn, MapX, plotBottom);
        var yTicks = yTickValues.Select(value => (plotLeft, mapY(value), formatValue(value))).ToList();
        var zeroLineY = scale == ChartScale.Linear ? mapY(0m) : (double?)null;

        return new ChartLayout(seriesPaths, xTicks, yTicks, width, height, zeroLineY);
    }

    private static (Func<decimal, double> MapY, IReadOnlyList<decimal> Ticks) BuildLinearScale(
        IEnumerable<decimal> values, double plotTop, double plotBottom)
    {
        var min = Math.Min(0m, values.Min());
        var max = Math.Max(0m, values.Max());
        if (max == min)
        {
            max = min + 1m; // плоский ряд (все точки равны, часто — все нули) — не делить на ноль
        }

        double MapY(decimal value) => plotBottom - (double)((value - min) / (max - min)) * (plotBottom - plotTop);

        var ticks = Enumerable.Range(0, YTickCount + 1)
            .Select(i => min + (max - min) * i / YTickCount)
            .ToList();

        return (MapY, ticks);
    }

    private static (Func<decimal, double> MapY, IReadOnlyList<decimal> Ticks) BuildLogarithmicScale(
        IEnumerable<decimal> values, double plotTop, double plotBottom)
    {
        var materialized = values.ToList();
        var positive = materialized.Where(v => v > 0m).ToList();
        // Пол шкалы — наименьшее реально встреченное положительное значение (на порядок ниже, для
        // запаса), а не константа: ряды остатков сильно отличаются по масштабу друг от друга.
        var floor = positive.Count > 0 ? positive.Min() / 10m : 0.1m;
        var max = Math.Max(floor * 10m, materialized.Count > 0 ? materialized.Max() : floor * 10m);

        var logFloor = Math.Log10((double)floor);
        var logMax = Math.Log10((double)max);
        if (logMax == logFloor)
        {
            logMax = logFloor + 1;
        }

        double MapY(decimal value)
        {
            var clamped = value <= floor ? floor : value;
            var logValue = Math.Log10((double)clamped);
            return plotBottom - (logValue - logFloor) / (logMax - logFloor) * (plotBottom - plotTop);
        }

        var ticks = Enumerable.Range(0, YTickCount + 1)
            .Select(i => (decimal)Math.Pow(10, logFloor + (logMax - logFloor) * i / YTickCount))
            .ToList();

        return (MapY, ticks);
    }

    private static IReadOnlyList<(double X, double Y, string Label)> BuildXTicks(
        int minTurn, int maxTurn, Func<int, double> mapX, double plotBottom)
    {
        var turnCount = maxTurn - minTurn + 1;
        var step = Math.Max(1, (int)Math.Ceiling(turnCount / (double)MaxXTickCount));

        var ticks = new List<(double, double, string)>();
        for (var turn = minTurn; turn <= maxTurn; turn += step)
        {
            ticks.Add((mapX(turn), plotBottom, turn.ToString(CultureInfo.InvariantCulture)));
        }

        if (ticks.Count == 0 || ticks[^1].Item3 != maxTurn.ToString(CultureInfo.InvariantCulture))
        {
            ticks.Add((mapX(maxTurn), plotBottom, maxTurn.ToString(CultureInfo.InvariantCulture)));
        }

        return ticks;
    }
}
