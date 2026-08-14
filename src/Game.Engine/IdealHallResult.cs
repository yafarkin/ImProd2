namespace Game.Engine;

/// <summary>Результат прогона идеального зала (Блок 7.3.4) — одна траектория X(t) на каждый сектор конфига.</summary>
public sealed record IdealHallResult
{
    public required IReadOnlyList<IdealHallBranchTrajectory> Branches { get; init; }
}
