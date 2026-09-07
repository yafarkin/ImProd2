using Game.Config;
using Game.Config.Loading;

namespace Game.Balancing;

/// <summary>
/// Автоподбор одного параметра-рычага (<see cref="CalibrationLever"/>) методом бисекции — та же
/// процедура, что человек делал руками весь rebalance/2-sector-stepwise (шаг за шагом двигать
/// наценку/BuildCost, смотреть X(t)/Score(t), повторять), только автоматически. Не линейное/целочисленное
/// программирование и не генетический алгоритм (запрос пользователя, явно спросил про них) — простой,
/// надёжный численный метод без производных: подходит именно потому, что метрика внутри нелинейна и не
/// дифференцируема (throttle — конечный автомат с порогами, ступени капремонта — целочисленный индекс,
/// компаундинг по ходам — X(t) зависит от X(t-1)), а ЛП/ЦЛП требуют линейности/выпуклости, которой тут
/// нет. Единственное требование — метрика МОНОТОННА по параметру на отрезке [min, max] (проверяется на
/// обеих границах перед бисекцией; если оба конца дают одинаковый знак «выше/ниже цели», взять в вилку
/// невозможно — сообщаем об этом честно, не гадаем и не выдаём случайный ответ).
/// </summary>
internal static class Calibrator
{
    /// <summary>Одна вычисленная точка поиска — для отчёта и трассировки.</summary>
    public sealed record IterationResult(int Iteration, decimal ParamValue, decimal MetricValue);

    /// <summary>
    /// Итог поиска. <see cref="Bracketed"/> = <see langword="false"/> — обе границы дали одинаковый
    /// знак разницы с целью, бисекция невозможна на этом отрезке (нужно раздвинуть <c>--calibrate-min</c>/
    /// <c>--calibrate-max</c> или переоценить, достижима ли цель этим рычагом вообще).
    /// </summary>
    public sealed record Result(
        bool Bracketed,
        decimal BestParamValue,
        decimal BestMetricValue,
        IReadOnlyList<IterationResult> Iterations);

    public static Result FindTarget(
        GameConfig baseConfig,
        Func<GameConfig, decimal, GameConfig> applyLever,
        Func<ResolvedGameConfig, decimal> evaluateMetric,
        decimal targetValue,
        decimal min,
        decimal max,
        decimal tolerance,
        int maxIterations,
        Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(baseConfig);
        ArgumentNullException.ThrowIfNull(applyLever);
        ArgumentNullException.ThrowIfNull(evaluateMetric);
        if (min >= max)
        {
            throw new ArgumentException($"'--calibrate-min' ({min}) должен быть меньше '--calibrate-max' ({max}).");
        }
        if (tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be positive.");
        }

        var iterations = new List<IterationResult>();

        decimal Evaluate(decimal paramValue)
        {
            var modified = applyLever(baseConfig, paramValue);
            var resolved = GameConfigLoader.Load(modified);
            var metric = evaluateMetric(resolved);
            iterations.Add(new IterationResult(iterations.Count + 1, paramValue, metric));
            trace?.Invoke($"итерация {iterations.Count}: параметр={paramValue:F4} -> метрика={metric:F0} (цель {targetValue:F0})");
            return metric;
        }

        var lowValue = min;
        var highValue = max;
        var lowMetric = Evaluate(lowValue);
        var highMetric = Evaluate(highValue);
        var lowDiff = lowMetric - targetValue;
        var highDiff = highMetric - targetValue;

        if (Math.Abs(lowDiff) <= tolerance)
        {
            return new Result(true, lowValue, lowMetric, iterations);
        }
        if (Math.Abs(highDiff) <= tolerance)
        {
            return new Result(true, highValue, highMetric, iterations);
        }
        if (Math.Sign(lowDiff) == Math.Sign(highDiff))
        {
            trace?.Invoke(
                $"не удалось взять цель в вилку: на обеих границах метрика по одну сторону от цели " +
                $"(мин={lowValue}->{lowMetric:F0}, макс={highValue}->{highMetric:F0}, цель={targetValue:F0}) — " +
                "раздвиньте --calibrate-min/--calibrate-max или проверьте, достижима ли цель этим рычагом.");
            var closest = lowDiff <= highDiff ? (lowValue, lowMetric) : (highValue, highMetric);
            return new Result(false, closest.Item1, closest.Item2, iterations);
        }

        for (var i = 0; i < maxIterations; i++)
        {
            var mid = (lowValue + highValue) / 2m;
            var midMetric = Evaluate(mid);
            var midDiff = midMetric - targetValue;

            if (Math.Abs(midDiff) <= tolerance)
            {
                return new Result(true, mid, midMetric, iterations);
            }

            if (Math.Sign(midDiff) == Math.Sign(lowDiff))
            {
                lowValue = mid;
                lowDiff = midDiff;
            }
            else
            {
                highValue = mid;
                highDiff = midDiff;
            }
        }

        // Исчерпали лимит итераций, так и не попав в допуск — возвращаем ближайшую из уже вычисленных
        // точек, не последнюю: середина отрезка на последнем шаге не обязана быть лучшей.
        var best = iterations.OrderBy(it => Math.Abs(it.MetricValue - targetValue)).First();
        return new Result(true, best.ParamValue, best.MetricValue, iterations);
    }
}
