using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Автораспределение новой команды по секторам (Блок 9.8, SPEC §9.6: «автораспределение в
/// наименее заполненный») — чистый калькулятор без обращения к состоянию сессии, годится и для
/// предпросмотра черновика ростера на экране администратора, и (при необходимости) для дальнейшего
/// перераспределения.
/// </summary>
public static class SectorAssignmentCalculator
{
    /// <summary>Сектор с наименьшим числом уже назначенных команд; при равенстве — первый по порядку в конфиге.</summary>
    public static Sector LeastFilled(IReadOnlyList<Sector> sectors, IReadOnlyList<TeamSpec> teams)
    {
        ArgumentNullException.ThrowIfNull(sectors);
        ArgumentNullException.ThrowIfNull(teams);
        if (sectors.Count == 0)
        {
            throw new ArgumentException("At least one sector is required.", nameof(sectors));
        }

        var counts = teams.GroupBy(t => t.SectorId).ToDictionary(g => g.Key, g => g.Count());
        return sectors.OrderBy(s => counts.GetValueOrDefault(s.Id, 0)).First();
    }
}
