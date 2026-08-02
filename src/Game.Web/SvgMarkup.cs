using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;

namespace Game.Web;

/// <summary>
/// Общие хелперы построения SVG-разметки для диаграмм (<see cref="MaterialChainDiagram"/>,
/// <see cref="FactoryChainDiagram"/>) — вынесены сюда, чтобы обход одной и той же особенности
/// Razor не дублировался в каждой странице.
/// </summary>
public static class SvgMarkup
{
    /// <summary>
    /// Число для SVG-атрибута геометрии (не текст для чтения человеком) — обязано идти с точкой как
    /// разделителем дробной части независимо от текущей культуры сервера, иначе на ru-RU (запятая)
    /// координаты ломают разбор d/x/y/width/height.
    /// </summary>
    public static string N(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Кубическая кривая между двумя точками с горизонтальными касательными — связи диаграмм.</summary>
    public static string CurvePath(double x1, double y1, double x2, double y2)
    {
        var midX = (x1 + x2) / 2;
        return $"M{N(x1)},{N(y1)} C{N(midX)},{N(y1)} {N(midX)},{N(y2)} {N(x2)},{N(y2)}";
    }

    /// <summary>
    /// SVG-элемент <c>&lt;text&gt;</c> как сырая разметка. Razor резервирует голый тег
    /// <c>&lt;text&gt;</c> для собственной разметочной конструкции (переход код/разметка) и не даёт
    /// повесить на него атрибуты (RZ1023) — единственный надёжный обход это собрать его в коде, а не
    /// как литеральный тег в @-разметке.
    /// </summary>
    public static MarkupString Text(double x, double y, string style, string content) =>
        new($"<text x=\"{N(x)}\" y=\"{N(y)}\" style=\"{style}\">{WebUtility.HtmlEncode(content)}</text>");
}
