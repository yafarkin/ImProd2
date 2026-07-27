namespace Game.Domain;

/// <summary>
/// Запись на доске потребностей (Блок 9.4, SPEC §9.2): команда добровольно сообщает об избытке или
/// дефиците материала. Содержимое не модерируется системой — недостоверность остаётся на совести
/// команды (наказывается репутацией на уровне игрового стола, не движковым механизмом).
/// </summary>
public sealed class NeedPosting
{
    /// <summary>Уникальный идентификатор записи.</summary>
    public Ulid Id { get; }

    /// <summary>Команда, опубликовавшая запись.</summary>
    public Ulid TeamId { get; }

    /// <summary>Материал, о котором идёт речь.</summary>
    public Material Material { get; }

    /// <summary>Избыток или дефицит.</summary>
    public NeedDirection Direction { get; }

    /// <summary>Грубый порядок объёма.</summary>
    public NeedVolumeOrder VolumeOrder { get; }

    /// <summary>Необязательный комментарий; пустая строка/пробелы приводятся к <c>null</c>.</summary>
    public string? Comment { get; }

    /// <summary>Текущий статус записи.</summary>
    public NeedStatus Status { get; private set; }

    public NeedPosting(
        Ulid id, Ulid teamId, Material material, NeedDirection direction, NeedVolumeOrder volumeOrder, string? comment)
    {
        if (id == Ulid.Empty)
        {
            throw new ArgumentException("Need posting id must not be empty.", nameof(id));
        }
        if (teamId == Ulid.Empty)
        {
            throw new ArgumentException("Team id must not be empty.", nameof(teamId));
        }
        ArgumentNullException.ThrowIfNull(material);

        Id = id;
        TeamId = teamId;
        Material = material;
        Direction = direction;
        VolumeOrder = volumeOrder;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        Status = NeedStatus.Active;
    }

    /// <summary>Отзывает запись — больше не показывается на доске.</summary>
    public void Withdraw()
    {
        if (Status != NeedStatus.Active)
        {
            throw new InvalidOperationException($"Cannot withdraw a posting in status '{Status}'.");
        }

        Status = NeedStatus.Withdrawn;
    }
}
