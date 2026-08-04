using System.Globalization;
using Game.Web;

namespace Game.Web.Tests;

/// <summary>Раскладка линейного графика для /team (Блок 9.1) — геометрические инварианты, без библиотеки чартов (см. doc-comment <see cref="LineChartDiagram"/>).</summary>
public class LineChartDiagramTests
{
    private static IReadOnlyList<(double X, double Y)> ParsePoints(string pathData) =>
        pathData
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token =>
            {
                var coords = token[1..].Split(',');
                return (double.Parse(coords[0], CultureInfo.InvariantCulture), double.Parse(coords[1], CultureInfo.InvariantCulture));
            })
            .ToList();

    [Fact]
    public void Build_Returns_An_Empty_Layout_When_There_Are_No_Points()
    {
        var layout = LineChartDiagram.Build([], LineChartDiagram.ChartScale.Linear, 400, 200);

        Assert.Empty(layout.Series);
        Assert.Empty(layout.XTicks);
        Assert.Empty(layout.YTicks);
    }

    [Fact]
    public void Build_Skips_Series_With_No_Points_But_Keeps_The_Others()
    {
        var series = new[]
        {
            new LineChartDiagram.ChartSeries("Пустой", "#000", []),
            new LineChartDiagram.ChartSeries("Руда", "#111", [(1, 5m), (2, 8m)]),
        };

        var layout = LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 400, 200);

        var only = Assert.Single(layout.Series);
        Assert.Equal("Руда", only.Label);
    }

    [Fact]
    public void Build_Orders_Points_By_Turn_Regardless_Of_Input_Order()
    {
        var series = new[]
        {
            new LineChartDiagram.ChartSeries("Руда", "#111", [(3, 30m), (1, 10m), (2, 20m)]),
        };

        var layout = LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 400, 200);

        var points = ParsePoints(layout.Series.Single().PathData);
        Assert.Equal(3, points.Count);
        Assert.True(points[0].X < points[1].X);
        Assert.True(points[1].X < points[2].X);
    }

    [Fact]
    public void Build_Handles_A_Single_Point_Without_Throwing()
    {
        var series = new[] { new LineChartDiagram.ChartSeries("Руда", "#111", [(1, 5m)]) };

        var layout = LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 400, 200);

        var points = ParsePoints(layout.Series.Single().PathData);
        Assert.Single(points);
    }

    [Fact]
    public void Logarithmic_Scale_Does_Not_Throw_When_A_Value_Is_Zero()
    {
        var series = new[]
        {
            new LineChartDiagram.ChartSeries("Руда", "#111", [(1, 0m), (2, 100m), (3, 10_000m)]),
        };

        var layout = LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Logarithmic, 400, 200);

        var points = ParsePoints(layout.Series.Single().PathData);
        Assert.All(points, p => Assert.False(double.IsNaN(p.Y) || double.IsInfinity(p.Y)));
    }

    [Fact]
    public void Logarithmic_Scale_Places_Larger_Values_Higher_On_The_Canvas()
    {
        var series = new[]
        {
            new LineChartDiagram.ChartSeries("Руда", "#111", [(1, 10m), (2, 10_000m)]),
        };

        var layout = LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Logarithmic, 400, 200);

        var points = ParsePoints(layout.Series.Single().PathData);
        // SVG: Y растёт вниз, поэтому большему значению соответствует меньший Y.
        Assert.True(points[1].Y < points[0].Y);
    }

    [Fact]
    public void All_Coordinates_Stay_Within_The_Requested_Canvas()
    {
        const double width = 400;
        const double height = 200;
        var series = new[]
        {
            new LineChartDiagram.ChartSeries("Руда", "#111", [(1, 1m), (2, 50_000m), (3, 3m)]),
        };

        var layout = LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Logarithmic, width, height);

        var points = ParsePoints(layout.Series.Single().PathData);
        Assert.All(points, p => Assert.InRange(p.X, 0, width));
        Assert.All(points, p => Assert.InRange(p.Y, 0, height));
        Assert.All(layout.YTicks, tick => Assert.InRange(tick.Y, 0, height));
    }

    [Fact]
    public void Linear_Scale_Exposes_A_Zero_Line_Coordinate_Between_Positive_And_Negative_Points()
    {
        // Запрос пользователя: жирная линия на нуле, чтобы чётко видеть, в плюсе команда или в
        // минусе — координата должна попадать строго между точками разных знаков.
        var series = new[]
        {
            new LineChartDiagram.ChartSeries("Баланс", "#111", [(1, 500m), (2, -500m)]),
        };

        var layout = LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 400, 200);

        var zeroLineY = Assert.NotNull(layout.ZeroLineY);
        var points = ParsePoints(layout.Series.Single().PathData);
        // SVG: Y растёт вниз — положительная точка выше (меньший Y), отрицательная ниже (больший Y).
        Assert.InRange(zeroLineY, points[0].Y, points[1].Y);
    }

    [Fact]
    public void Logarithmic_Scale_Has_No_Zero_Line()
    {
        var series = new[] { new LineChartDiagram.ChartSeries("Руда", "#111", [(1, 10m), (2, 100m)]) };

        var layout = LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Logarithmic, 400, 200);

        Assert.Null(layout.ZeroLineY);
    }

    [Fact]
    public void Build_Uses_The_Provided_Formatter_For_The_Last_Point_Label()
    {
        var series = new[] { new LineChartDiagram.ChartSeries("Прибыль", "#111", [(1, 5m), (2, 1234m)]) };

        var layout = LineChartDiagram.Build(
            series, LineChartDiagram.ChartScale.Linear, 400, 200, value => $"₽{value:0}");

        Assert.Equal("₽1234", layout.Series.Single().LastValueLabel);
    }
}
