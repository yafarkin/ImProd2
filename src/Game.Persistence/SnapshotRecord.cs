namespace Game.Persistence;

/// <summary>
/// Содержимое файла снапшота: состояние сессии на момент снимка плюс номер последней записи
/// журнала, чей эффект в нём уже учтён — восстановление доигрывает только записи после неё.
/// </summary>
internal sealed record SnapshotRecord<TState>
{
    /// <summary>Номер (<see cref="Game.Engine.EventLogEntry{TState}.SequenceNumber"/>) последней применённой записи; -1, если снапшот снят до первого события.</summary>
    public required int SequenceNumber { get; init; }

    /// <summary>Состояние сессии на момент снимка.</summary>
    public required TState State { get; init; }
}
