using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components;

namespace Game.Web;

/// <summary>
/// Рисует <see cref="LineChartDiagram.ChartLayout"/> целиком (сетка, оси, линии рядов, подпись
/// последней точки, легенда при ≥2 рядах) как сырую SVG-разметку — общий код для всех страниц с
/// графиками (/team, /screen), чтобы не дублировать одну и ту же сборку разметки построчно.
/// </summary>
public static class ChartRenderer
{
    public static MarkupString Render(LineChartDiagram.ChartLayout layout)
    {
        if (layout.Series.Count == 0)
        {
            return new MarkupString("<p><em>Пока нет данных для графика — данные появятся после первого хода.</em></p>");
        }

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg width=\"{SvgMarkup.N(layout.Width)}\" height=\"{SvgMarkup.N(layout.Height)}\" viewBox=\"0 0 {SvgMarkup.N(layout.Width)} {SvgMarkup.N(layout.Height)}\" style=\"max-width:100%;height:auto;border:1px solid #e1e0d9\">");

        foreach (var tick in layout.YTicks)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"0\" y1=\"{SvgMarkup.N(tick.Y)}\" x2=\"{SvgMarkup.N(layout.Width)}\" y2=\"{SvgMarkup.N(tick.Y)}\" stroke=\"#e1e0d9\" stroke-width=\"1\" />");
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"2\" y=\"{SvgMarkup.N(tick.Y - 3)}\" style=\"font-size:10px;fill:#898781\">{WebUtility.HtmlEncode(tick.Label)}</text>");
        }

        foreach (var tick in layout.XTicks)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{SvgMarkup.N(tick.X)}\" y=\"{SvgMarkup.N(tick.Y + 14)}\" style=\"font-size:10px;fill:#898781;text-anchor:middle\">{WebUtility.HtmlEncode(tick.Label)}</text>");
        }

        // Отдельная жирная линия на нуле поверх обычной сетки (запрос пользователя: «чётко видно, в
        // плюсе команда или в минусе») — рисуется до рядов, чтобы сами линии данных были поверх неё,
        // а не наоборот.
        if (layout.ZeroLineY is { } zeroLineY)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"0\" y1=\"{SvgMarkup.N(zeroLineY)}\" x2=\"{SvgMarkup.N(layout.Width)}\" y2=\"{SvgMarkup.N(zeroLineY)}\" stroke=\"#52514e\" stroke-width=\"2\" />");
        }

        foreach (var series in layout.Series)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{series.PathData}\" fill=\"none\" stroke=\"{series.Color}\" stroke-width=\"2\" />");
            svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{SvgMarkup.N(series.LastX)}\" cy=\"{SvgMarkup.N(series.LastY)}\" r=\"3\" fill=\"{series.Color}\" />");
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{SvgMarkup.N(series.LastX + 6)}\" y=\"{SvgMarkup.N(series.LastY + 4)}\" style=\"font-size:11px;font-weight:600;fill:{series.Color}\">{WebUtility.HtmlEncode(series.LastValueLabel)}</text>");
        }

        svg.Append("</svg>");

        if (layout.Series.Count > 1)
        {
            svg.Append("<div style=\"display:flex;flex-wrap:wrap;gap:.75rem;font-size:11px;color:#52514e;margin-top:.25rem\">");
            foreach (var series in layout.Series)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<span><span style=\"display:inline-block;width:10px;height:10px;background:{series.Color};margin-right:4px;border-radius:2px\"></span>{WebUtility.HtmlEncode(series.Label)}</span>");
            }
            svg.Append("</div>");
        }

        return new MarkupString(svg.ToString());
    }
}
