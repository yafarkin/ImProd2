namespace Game.Domain;

/// <summary>Направление записи на доске потребностей (SPEC §9.2): избыток или дефицит материала.</summary>
public enum NeedDirection
{
    /// <summary>У команды избыток материала — есть чем поделиться.</summary>
    Surplus,

    /// <summary>У команды дефицит материала — команда ищет его.</summary>
    Deficit
}
