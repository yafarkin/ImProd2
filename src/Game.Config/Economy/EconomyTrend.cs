using System.Text.Json.Serialization;

namespace Game.Config.Economy;

/// <summary>Сценарный тренд внешней экономики сессии (SPEC §5.4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EconomyTrend
{
    /// <summary>Подъём: цена и ёмкость растут.</summary>
    Up,

    /// <summary>Стабильность: цена и ёмкость не меняются трендом.</summary>
    Stable,

    /// <summary>Спад: цена и ёмкость падают.</summary>
    Down,
}
