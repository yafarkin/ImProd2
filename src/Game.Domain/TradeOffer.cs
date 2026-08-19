namespace Game.Domain;

/// <summary>
/// Запись на доске публичных заявок для межсекторной торговли ботов (запрос пользователя
/// 2026-08-17): команда объявляет, что хочет продать или купить материал, регулярно за ход или
/// разово, по цене в заявленном диапазоне — не бесплатный слух, как <see cref="NeedPosting"/>, а
/// твёрдое предложение сделки, которое любая другая команда может исполнить как есть, без
/// переговоров (у ботов их и не будет). Диапазон цены — прямая замена настоящему торгу: одна сторона
/// не может доторговаться до конкретного числа с другой, поэтому сама называет вилку, приемлемую ей
/// заранее. Заявка живёт не дольше <see cref="MaxAgeInTurns"/> ходов — дальше её нельзя исполнить,
/// даже если формально она ещё <see cref="TradeOfferStatus.Open"/> (см. <see
/// cref="Game.Engine.GameSession.FulfillTradeOffer"/>).
/// </summary>
public sealed class TradeOffer
{
    /// <summary>Сколько ходов, включая ход публикации, заявка остаётся исполнимой.</summary>
    public const int MaxAgeInTurns = 3;

    /// <summary>Уникальный идентификатор заявки.</summary>
    public Ulid Id { get; }

    /// <summary>Команда, опубликовавшая заявку.</summary>
    public Ulid TeamId { get; }

    /// <summary>Продаёт или покупает публикующая команда.</summary>
    public TradeOfferDirection Direction { get; }

    /// <summary>Материал заявки.</summary>
    public Material Material { get; }

    /// <summary>Разовая поставка или регулярные поставки каждый ход, пока заявку не исполнят.</summary>
    public ContractType Type { get; }

    /// <summary>Объём — за одну поставку (в т.ч. для <see cref="ContractType.Recurring"/>, тот же смысл, что и у <see cref="ContractTerms.Volume"/>).</summary>
    public decimal Volume { get; }

    /// <summary>Минимально приемлемая цена за единицу для стороны, публикующей заявку.</summary>
    public decimal MinPrice { get; }

    /// <summary>Максимально приемлемая цена за единицу для стороны, публикующей заявку.</summary>
    public decimal MaxPrice { get; }

    /// <summary>Ход, на котором заявка опубликована — точка отсчёта для <see cref="MaxAgeInTurns"/>.</summary>
    public int PostedTurn { get; }

    /// <summary>Текущий статус заявки.</summary>
    public TradeOfferStatus Status { get; private set; }

    public TradeOffer(
        Ulid id, Ulid teamId, TradeOfferDirection direction, Material material, ContractType type,
        decimal volume, decimal minPrice, decimal maxPrice, int postedTurn)
    {
        if (id == Ulid.Empty)
        {
            throw new ArgumentException("Trade offer id must not be empty.", nameof(id));
        }
        if (teamId == Ulid.Empty)
        {
            throw new ArgumentException("Team id must not be empty.", nameof(teamId));
        }
        ArgumentNullException.ThrowIfNull(material);
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Trade offer volume must be positive.");
        }
        if (minPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minPrice), minPrice, "Minimum price must be positive.");
        }
        if (maxPrice < minPrice)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPrice), maxPrice, "Maximum price must not be below the minimum price.");
        }
        if (postedTurn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(postedTurn), postedTurn, "Posted turn must be positive.");
        }

        Id = id;
        TeamId = teamId;
        Direction = direction;
        Material = material;
        Type = type;
        Volume = volume;
        MinPrice = minPrice;
        MaxPrice = maxPrice;
        PostedTurn = postedTurn;
        Status = TradeOfferStatus.Open;
    }

    /// <summary>Последний ход, на котором заявка ещё исполнима.</summary>
    public int ExpiresAfterTurn => PostedTurn + MaxAgeInTurns - 1;

    /// <summary>Заявка ещё видна и исполнима на ход <paramref name="currentTurn"/>.</summary>
    public bool IsOpenOn(int currentTurn) => Status == TradeOfferStatus.Open && currentTurn <= ExpiresAfterTurn;

    /// <summary>Помечает заявку исполненной — по ней заключён контракт.</summary>
    public void Fulfill()
    {
        if (Status != TradeOfferStatus.Open)
        {
            throw new InvalidOperationException($"Cannot fulfill a trade offer in status '{Status}'.");
        }

        Status = TradeOfferStatus.Fulfilled;
    }

    /// <summary>Отзывает заявку — больше не показывается на доске и не может быть исполнена.</summary>
    public void Withdraw()
    {
        if (Status != TradeOfferStatus.Open)
        {
            throw new InvalidOperationException($"Cannot withdraw a trade offer in status '{Status}'.");
        }

        Status = TradeOfferStatus.Withdrawn;
    }
}
