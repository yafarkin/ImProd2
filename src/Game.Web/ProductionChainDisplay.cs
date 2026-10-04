namespace Game.Web;

/// <summary>
/// Подписи цепочки «Производства» (<see cref="ProductionChain"/>) — отдельно от разметки, чтобы
/// формулировки сторожили тесты. Только факты, без повелительного наклонения (то же правило, что у
/// панели «Требует внимания»).
/// </summary>
public static class ProductionChainDisplay
{
    /// <summary>
    /// Движение материала за прошлый ход: «+3610 · −3400 Агломерационная фабрика». Склад общий и стоит
    /// между переделами, поэтому важнее всего видно, кто материал забирает, — и что его не забирает
    /// никто (§2 п.8: руда копилась тысячами, и со склада этого видно не было).
    /// </summary>
    public static string FlowText(ProductionChain.StockLine line, bool hasLastTurn)
    {
        ArgumentNullException.ThrowIfNull(line);

        var parts = new List<string>();
        if (line.ProducerNames.Count == 0)
        {
            parts.Add("не производит ни одна ваша фабрика");
        }
        else
        {
            parts.Add(hasLastTurn
                ? $"+{line.ProducedLastTurn:0.##}"
                : $"производит: {string.Join(", ", line.ProducerNames)}");
        }

        if (line.ConsumedLastTurn.Count == 0)
        {
            parts.Add("не потребляет ни одна ваша фабрика");
        }
        else if (hasLastTurn)
        {
            parts.AddRange(line.ConsumedLastTurn.Select(consumer => $"−{consumer.Quantity:0.##} {consumer.Name}"));
        }
        else
        {
            parts.Add($"потребляет: {string.Join(", ", line.ConsumerNames)}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Сводка свёрнутого передела: «3 фабрики · выпуск 1700 ед./ход». Число проблем показывает
    /// разметка отдельно, цветом.
    /// </summary>
    public static string LevelSummary(ProductionChain.Level level, bool hasLastTurn)
    {
        ArgumentNullException.ThrowIfNull(level);

        if (level.FactoryCount == 0)
        {
            return "не построено";
        }

        var factories = DashboardDisplay.Factories(level.FactoryCount);
        return hasLastTurn ? $"{factories} · выпуск {level.LastTurnOutput:0.##} ед./ход" : factories;
    }

    /// <summary>Подпись группы однотипных фабрик: «30 рабочих · хуже всех: состояние 61 %».</summary>
    public static string TypeGroupSubtitle(ProductionChain.TypeGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return $"{group.Workers} {Workers(group.Workers)} · хуже всех: состояние {group.WorstCondition:P0}";
    }

    /// <summary>Подпись экземпляра: «10 рабочих · состояние 88 %» или «… · на капремонте».</summary>
    public static string InstanceSubtitle(ProductionChain.Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var text = $"{instance.Workers} {Workers(instance.Workers)} · состояние {instance.Condition:P0}";
        return instance.IsUnderRepair ? text + " · на капремонте" : text;
    }

    /// <summary>
    /// Выпуск экземпляра или группы: «1251» на полной мощности, «1108 из 1251» — ниже потолка;
    /// <c>null</c> — расчётов ещё не было.
    /// </summary>
    public static string? OutputText(decimal? lastTurnOutput, decimal maxOutput)
    {
        if (lastTurnOutput is not { } output)
        {
            return null;
        }

        return output < maxOutput ? $"{output:0.##} из {maxOutput:0.##}" : $"{output:0.##}";
    }

    /// <summary>«1 рабочий», «3 рабочих», «21 рабочий» — после числа в родительном падеже множественного числа всё, кроме единицы.</summary>
    public static string Workers(int count)
    {
        var lastTwo = Math.Abs(count) % 100;
        return lastTwo % 10 == 1 && lastTwo != 11 ? "рабочий" : "рабочих";
    }
}
