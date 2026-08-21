using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>Итог попытки исполнить одну <see cref="BotCommand"/> над сессией.</summary>
public abstract record BotCommandExecutionResult
{
    private BotCommandExecutionResult()
    {
    }

    /// <summary>Команда исполнена, изменение записано в журнал сессии.</summary>
    public sealed record Success(EventLogEntry<GameSessionState> Entry) : BotCommandExecutionResult;

    /// <summary>Бот попросил ничего не делать — не ошибка, просто пустой ход.</summary>
    public sealed record Nop : BotCommandExecutionResult;

    /// <summary>
    /// Команда не прошла валидацию — либо не хватает обязательных полей, либо сама
    /// <see cref="GameSession"/> отказала (её обычные <see cref="ArgumentException"/>/
    /// <see cref="InvalidOperationException"/>, текст на английском). Текст уходит обратно в
    /// промпт на следующей попытке (<see cref="LlmBotDecisionLoop"/>).
    /// </summary>
    public sealed record DomainError(string Message) : BotCommandExecutionResult;
}

/// <summary>
/// Переводит одну <see cref="BotCommand"/> в вызов существующего командного API
/// <see cref="GameSession"/> — того же слоя, которым пользуется человек через Game.Web и
/// формульный SimpleBot. Здесь нет ни одного правила экономики, только диспетчеризация по
/// <see cref="BotCommandKind"/> и перевод исключений валидации в текст для ретрая.
/// </summary>
public sealed class BotCommandExecutor
{
    /// <summary>
    /// Исполняет команду для данной команды-игрока; никогда не бросает исключение — любая ошибка
    /// возвращается как <see cref="BotCommandExecutionResult.DomainError"/>. <paramref name="random"/>
    /// используется только для <see cref="BotCommandKind.FulfillTradeOffer"/> (код подтверждения
    /// контракта, см. <see cref="ExecuteFulfillTradeOffer"/>) — тот же общий на весь прогон
    /// генератор, что и у <see cref="Game.Engine.GameSession.RunTick"/>, ради воспроизводимости
    /// журнала (AGENTS §2, правило 6).
    /// </summary>
    public BotCommandExecutionResult Execute(BotCommand command, GameSession session, Ulid teamId, Random random)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(random);

        try
        {
            return command.Kind switch
            {
                BotCommandKind.Nop => new BotCommandExecutionResult.Nop(),
                BotCommandKind.BuildFactory => ExecuteBuildFactory(command, session, teamId),
                BotCommandKind.SetWorkerCount => ExecuteSetWorkerCount(command, session, teamId),
                BotCommandKind.SelectRecipe => ExecuteSelectRecipe(command, session, teamId),
                BotCommandKind.SetRndCommitment => ExecuteSetRndCommitment(command, session, teamId),
                BotCommandKind.SetGenerationResearchCommitment => ExecuteSetGenerationResearchCommitment(command, session, teamId),
                BotCommandKind.SetOverhaulRequested => ExecuteSetOverhaulRequested(command, session, teamId),
                BotCommandKind.SellToSystem => ExecuteSellToSystem(command, session, teamId),
                BotCommandKind.SellFactory => ExecuteSellFactory(command, session, teamId),
                BotCommandKind.SetFactoryAllocationShare => ExecuteSetFactoryAllocationShare(command, session, teamId),
                BotCommandKind.PostNeed => ExecutePostNeed(command, session, teamId),
                BotCommandKind.WithdrawNeed => ExecuteWithdrawNeed(command, session, teamId),
                BotCommandKind.EmergencyPurchase => ExecuteEmergencyPurchase(command, session, teamId),
                BotCommandKind.PostSellOffer => ExecutePostTradeOffer(command, session, teamId, TradeOfferDirection.Sell),
                BotCommandKind.PostBuyOffer => ExecutePostTradeOffer(command, session, teamId, TradeOfferDirection.Buy),
                BotCommandKind.WithdrawTradeOffer => ExecuteWithdrawTradeOffer(command, session, teamId),
                BotCommandKind.FulfillTradeOffer => ExecuteFulfillTradeOffer(command, session, teamId, random),
                _ => new BotCommandExecutionResult.DomainError($"Unknown command kind '{command.Kind}'."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new BotCommandExecutionResult.DomainError(ex.Message);
        }
    }

    private static BotCommandExecutionResult ExecuteBuildFactory(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.FactoryDefinitionId is null)
        {
            return new BotCommandExecutionResult.DomainError("BuildFactory requires factoryDefinitionId.");
        }

        return new BotCommandExecutionResult.Success(session.BuildFactory(teamId, command.FactoryDefinitionId, command.RecipeId));
    }

    private static BotCommandExecutionResult ExecuteSetWorkerCount(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.FactoryId is not { } factoryId || command.Count is not { } count)
        {
            return new BotCommandExecutionResult.DomainError("SetWorkerCount requires factoryId and count.");
        }

        return new BotCommandExecutionResult.Success(session.SetWorkerCount(teamId, factoryId, count));
    }

    private static BotCommandExecutionResult ExecuteSelectRecipe(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.FactoryId is not { } factoryId || command.RecipeId is null)
        {
            return new BotCommandExecutionResult.DomainError("SelectRecipe requires factoryId and recipeId.");
        }

        return new BotCommandExecutionResult.Success(session.SelectRecipe(teamId, factoryId, command.RecipeId));
    }

    private static BotCommandExecutionResult ExecuteSetRndCommitment(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.FactoryId is not { } factoryId || command.Amount is not { } amount)
        {
            return new BotCommandExecutionResult.DomainError("SetRndCommitment requires factoryId and amount.");
        }

        return new BotCommandExecutionResult.Success(session.SetRndCommitment(teamId, factoryId, amount));
    }

    private static BotCommandExecutionResult ExecuteSetGenerationResearchCommitment(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.Amount is not { } amount)
        {
            return new BotCommandExecutionResult.DomainError("SetGenerationResearchCommitment requires amount.");
        }

        return new BotCommandExecutionResult.Success(session.SetGenerationResearchCommitment(teamId, amount));
    }

    private static BotCommandExecutionResult ExecuteSetOverhaulRequested(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.FactoryId is not { } factoryId || command.Enabled is not { } enabled)
        {
            return new BotCommandExecutionResult.DomainError("SetOverhaulRequested requires factoryId and enabled.");
        }

        return new BotCommandExecutionResult.Success(session.SetOverhaulRequested(teamId, factoryId, enabled));
    }

    private static BotCommandExecutionResult ExecuteSellToSystem(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.MaterialId is null || command.Volume is not { } volume)
        {
            return new BotCommandExecutionResult.DomainError("SellToSystem requires materialId and volume.");
        }

        return new BotCommandExecutionResult.Success(session.SellToSystem(teamId, command.MaterialId, volume));
    }

    private static BotCommandExecutionResult ExecuteSellFactory(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.FactoryId is not { } factoryId)
        {
            return new BotCommandExecutionResult.DomainError("SellFactory requires factoryId.");
        }

        return new BotCommandExecutionResult.Success(session.SellFactory(teamId, factoryId));
    }

    private static BotCommandExecutionResult ExecuteSetFactoryAllocationShare(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.FactoryId is not { } factoryId || command.Share is not { } share)
        {
            return new BotCommandExecutionResult.DomainError("SetFactoryAllocationShare requires factoryId and share.");
        }

        return new BotCommandExecutionResult.Success(session.SetFactoryAllocationShare(teamId, factoryId, share));
    }

    private static BotCommandExecutionResult ExecutePostNeed(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.MaterialId is null || command.Direction is null || command.VolumeOrder is null)
        {
            return new BotCommandExecutionResult.DomainError("PostNeed requires materialId, direction, and volumeOrder.");
        }
        if (!TryParseDirection(command.Direction, out var direction))
        {
            return new BotCommandExecutionResult.DomainError(
                $"PostNeed: unknown direction '{command.Direction}', expected 'surplus' or 'deficit'.");
        }
        if (!TryParseVolumeOrder(command.VolumeOrder, out var volumeOrder))
        {
            return new BotCommandExecutionResult.DomainError(
                $"PostNeed: unknown volumeOrder '{command.VolumeOrder}', expected 'small', 'medium', or 'large'.");
        }

        return new BotCommandExecutionResult.Success(session.PostNeed(teamId, command.MaterialId, direction, volumeOrder, command.Comment));
    }

    private static BotCommandExecutionResult ExecuteWithdrawNeed(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.NeedId is not { } needId)
        {
            return new BotCommandExecutionResult.DomainError("WithdrawNeed requires needId.");
        }

        return new BotCommandExecutionResult.Success(session.WithdrawNeed(teamId, needId));
    }

    private static BotCommandExecutionResult ExecuteEmergencyPurchase(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.MaterialId is null || command.Volume is not { } volume)
        {
            return new BotCommandExecutionResult.DomainError("EmergencyPurchase requires materialId and volume.");
        }

        return new BotCommandExecutionResult.Success(session.EmergencyPurchase(teamId, command.MaterialId, volume));
    }

    private static BotCommandExecutionResult ExecutePostTradeOffer(BotCommand command, GameSession session, Ulid teamId, TradeOfferDirection direction)
    {
        if (command.MaterialId is null || command.Volume is not { } volume || command.MinPrice is not { } minPrice || command.MaxPrice is not { } maxPrice)
        {
            return new BotCommandExecutionResult.DomainError("PostSellOffer/PostBuyOffer requires materialId, volume, minPrice, and maxPrice.");
        }

        var type = command.Recurring == true ? ContractType.Recurring : ContractType.Spot;
        return new BotCommandExecutionResult.Success(session.PostTradeOffer(teamId, direction, command.MaterialId, type, volume, minPrice, maxPrice));
    }

    private static BotCommandExecutionResult ExecuteWithdrawTradeOffer(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.TradeOfferId is not { } tradeOfferId)
        {
            return new BotCommandExecutionResult.DomainError("WithdrawTradeOffer requires tradeOfferId.");
        }

        return new BotCommandExecutionResult.Success(session.WithdrawTradeOffer(teamId, tradeOfferId));
    }

    /// <summary>
    /// Исполняет чужую заявку с доски публичных заявок: собирает контракт на условиях заявки и сразу
    /// сводит+подтверждает его тем же приёмом, что и <c>Game.Bots.OrderBook.SignContract</c> для
    /// механического стакана SimpleBot — продавец «подаёт» заявку A (инициатор), покупатель
    /// подтверждает; здесь эта роль всегда достаётся стороне, исполняющей чужое предложение, не
    /// автору заявки (он уже выразил согласие самим фактом публикации).
    /// <para>
    /// <paramref name="command"/>.Volume/UnitPrice — опциональны, а не обязательны, хотя раньше были
    /// обязательными: живой прогон 2026-08-20 (<c>_2bot_gpt_oss_20b_2stage_v4</c>) — 37 попыток
    /// <c>fulfillTradeOffer</c> за один прогон (ACTION SUGGESTIONS сработала, модель явно захотела
    /// закрывать сделки), и ВСЕ 37 отклонены одной и той же ошибкой «требуются tradeOfferId, volume и
    /// unitPrice»: <c>tradeOfferId</c> модель называла верно каждый раз, а из оставшихся двух полей
    /// упорно теряла то одно, то другое в одном вызове. Три обязательных поля разом оказались
    /// систематически недостижимы для этой модели, не единичный сбой. Раз почти вся нужная информация
    /// уже публична (сама заявка целиком видна на доске), не наказываем недописанный вызов отказом —
    /// достраиваем разумным умолчанием: <c>volume</c> — вся заявка целиком, <c>unitPrice</c> —
    /// середина её ценового диапазона (никого не обделяет специально, ни автора заявки, ни того, кто
    /// её исполняет). <c>tradeOfferId</c> по-прежнему обязателен — единственное поле, которое нельзя
    /// вывести ни из чего другого, и единственное, с которым модель ни разу не ошиблась.
    /// </para>
    /// </summary>
    private static BotCommandExecutionResult ExecuteFulfillTradeOffer(BotCommand command, GameSession session, Ulid teamId, Random random)
    {
        if (command.TradeOfferId is not { } tradeOfferId)
        {
            return new BotCommandExecutionResult.DomainError("FulfillTradeOffer requires tradeOfferId.");
        }
        if (!session.State.TradeOffers.TryGetValue(tradeOfferId, out var offer))
        {
            return new BotCommandExecutionResult.DomainError($"Unknown trade offer '{tradeOfferId}'.");
        }
        if (!offer.IsOpenOn(session.State.CurrentTurn))
        {
            return new BotCommandExecutionResult.DomainError($"Trade offer '{tradeOfferId}' is no longer open (expired or already fulfilled).");
        }
        if (offer.TeamId == teamId)
        {
            return new BotCommandExecutionResult.DomainError("A team cannot fulfill its own trade offer.");
        }

        var volume = command.Volume ?? offer.Volume;
        var unitPrice = command.UnitPrice ?? (offer.MinPrice + offer.MaxPrice) / 2m;

        if (volume <= 0 || volume > offer.Volume)
        {
            return new BotCommandExecutionResult.DomainError($"FulfillTradeOffer volume must be positive and at most the offer's volume ({offer.Volume}).");
        }
        if (unitPrice < offer.MinPrice || unitPrice > offer.MaxPrice)
        {
            return new BotCommandExecutionResult.DomainError($"FulfillTradeOffer unitPrice must be between {offer.MinPrice} and {offer.MaxPrice}.");
        }

        var (buyerTeamId, sellerTeamId) = offer.Direction == TradeOfferDirection.Sell ? (teamId, offer.TeamId) : (offer.TeamId, teamId);
        var turn = session.State.CurrentTurn;
        var penaltyRate = session.State.Config.Raw.Contracts.DeliveryMissPenaltyRate;
        var terms = offer.Type == ContractType.Spot
            ? new ContractTerms(ContractType.Spot, offer.Material, volume, unitPrice, penaltyRate, effectiveTurn: turn, spotDeliveryTurn: turn + 1, recurringEndTurn: null)
            : new ContractTerms(ContractType.Recurring, offer.Material, volume, unitPrice, penaltyRate, effectiveTurn: turn, spotDeliveryTurn: null, recurringEndTurn: null);

        var sellerProposal = new ContractProposal(buyerTeamId, sellerTeamId, sellerTeamId, terms);
        var buyerProposal = new ContractProposal(buyerTeamId, sellerTeamId, buyerTeamId, terms);

        var formation = session.SubmitContractProposals(sellerProposal, buyerProposal, random);
        if (!formation.IsMatched)
        {
            return new BotCommandExecutionResult.DomainError(
                $"Could not form a contract from trade offer '{tradeOfferId}': {string.Join(", ", formation.Mismatches)}.");
        }

        session.ConfirmContract(formation.Contract!.Id, TeamRole.Manager, buyerTeamId);
        return new BotCommandExecutionResult.Success(session.MarkTradeOfferFulfilled(tradeOfferId, teamId));
    }

    private static bool TryParseDirection(string value, out NeedDirection direction)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "surplus":
                direction = NeedDirection.Surplus;
                return true;
            case "deficit":
                direction = NeedDirection.Deficit;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    private static bool TryParseVolumeOrder(string value, out NeedVolumeOrder volumeOrder)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "small":
                volumeOrder = NeedVolumeOrder.Small;
                return true;
            case "medium":
                volumeOrder = NeedVolumeOrder.Medium;
                return true;
            case "large":
                volumeOrder = NeedVolumeOrder.Large;
                return true;
            default:
                volumeOrder = default;
                return false;
        }
    }
}
