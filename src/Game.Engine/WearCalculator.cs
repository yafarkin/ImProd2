using Game.Config.Economy;

namespace Game.Engine;

/// <summary>
/// Чистые функции расчёта износа фабрики (SPEC §5.6): скорость декея сама ускоряется с возрастом
/// сверх льготного периода — незаметно в первые ходы, резко тяжелее дальше (запрос пользователя: не
/// должно вырождаться в «один раз задекларировал сумму капремонта и забыл»). Не решает, что делать
/// при пересечении критического состояния — это ответственность <see cref="WearStep"/>, который
/// строит из результатов этих функций события (переход в простой либо рутинное изменение состояния).
/// </summary>
public static class WearCalculator
{
    /// <summary>
    /// Возраст фабрики сверх льготного периода — «t» в формуле <see cref="CalculateDecayRate"/>.
    /// Может быть неположительным (льгота ещё не кончилась) — вызывающий код не обязан отсекать это
    /// сам, <see cref="CalculateDecayRate"/> сам возвращает 0 для таких значений.
    /// </summary>
    public static int CalculateAgeBeyondGrace(int lastResetTurn, int currentTurn, int gracePeriodTurns) =>
        currentTurn - lastResetTurn - gracePeriodTurns;

    /// <summary>
    /// Скорость снижения <c>Factory.Condition</c> за этот ход: 0 в льготный период, дальше растёт
    /// линейно с возрастом сверх льготы (<c>BaseWearRatePerTurn + AccelerationFactorPerTurn × t</c>) —
    /// сама механика не константна, а ускоряется.
    /// </summary>
    public static decimal CalculateDecayRate(int ageBeyondGrace, WearConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return ageBeyondGrace > 0 ? config.BaseWearRatePerTurn + config.AccelerationFactorPerTurn * ageBeyondGrace : 0m;
    }

    /// <summary>Фабрика в идеальном состоянии — капремонт заказывать не на что (нечего чинить).</summary>
    public static bool IsFullyRestored(decimal condition) => condition >= 1m;

    /// <summary>
    /// Состояние после рутинного декея этого хода. Клэмп по [0, 1] снизу защитный (декей не может
    /// утопить ниже 0); клэмп по критическому порогу здесь намеренно не делается — решение «пора в
    /// вынужденный простой» принимает <see cref="WearStep"/>, читая непосредственно это значение.
    /// </summary>
    public static decimal CalculateNextCondition(decimal condition, decimal decayRate) =>
        Math.Clamp(condition - decayRate, 0m, 1m);

    /// <summary>
    /// Какая ступень капремонта (SPEC §5.6) сработает, если команда закажет его при данном состоянии
    /// фабрики — первая по списку (упорядоченному по убыванию <see
    /// cref="OverhaulTierConfig.MinCondition"/>), чьему порогу удовлетворяет <paramref
    /// name="condition"/>. <see langword="null"/>, если ни одна не подходит (состояние уже ниже
    /// нижней границы самой слабой ступени — на практике не должно случаться, пока <see
    /// cref="WearConfig.CriticalConditionThreshold"/> не ниже этой границы: движок сам останавливает
    /// фабрику раньше, см. <see cref="IsCritical"/>).
    /// </summary>
    public static OverhaulTierConfig? SelectTier(decimal condition, IReadOnlyList<OverhaulTierConfig> tiers)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        return tiers.FirstOrDefault(tier => condition >= tier.MinCondition);
    }

    /// <summary>
    /// Множитель штрафа к <c>FactoryUpkeepPaid</c> — линейная интерполяция от ×1.0 при
    /// <c>Condition = 1</c> до <c>×(1 + MaxUpkeepPenaltyMultiplier)</c> при
    /// <c>Condition = CriticalConditionThreshold</c>: предупреждающее давление на бюджет, нарастающее
    /// ещё до того, как фабрика уйдёт в простой.
    /// </summary>
    public static decimal CalculateUpkeepPenaltyMultiplier(decimal condition, WearConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var range = 1m - config.CriticalConditionThreshold;
        if (range <= 0m)
        {
            return 1m;
        }

        var wearFraction = Math.Clamp((1m - condition) / range, 0m, 1m);
        return 1m + wearFraction * config.MaxUpkeepPenaltyMultiplier;
    }

    /// <summary>Состояние пересекло критический порог — фабрике пора в вынужденный простой.</summary>
    public static bool IsCritical(decimal condition, WearConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return condition <= config.CriticalConditionThreshold;
    }
}
