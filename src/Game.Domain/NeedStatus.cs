namespace Game.Domain;

/// <summary>Статус записи на доске потребностей (SPEC §9.2).</summary>
public enum NeedStatus
{
    /// <summary>Запись видна на доске.</summary>
    Active,

    /// <summary>Команда отозвала запись — на доске больше не видна.</summary>
    Withdrawn
}
