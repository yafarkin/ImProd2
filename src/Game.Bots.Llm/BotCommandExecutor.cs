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
    /// <summary>Исполняет команду для данной команды-игрока; никогда не бросает исключение — любая ошибка возвращается как <see cref="BotCommandExecutionResult.DomainError"/>.</summary>
    public BotCommandExecutionResult Execute(BotCommand command, GameSession session, Ulid teamId)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(session);

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
                BotCommandKind.TakeLoan => ExecuteTakeLoan(command, session, teamId),
                BotCommandKind.RepayLoan => ExecuteRepayLoan(command, session, teamId),
                BotCommandKind.SellToSystem => ExecuteSellToSystem(command, session, teamId),
                BotCommandKind.SellFactory => ExecuteSellFactory(command, session, teamId),
                BotCommandKind.SetFactoryAllocationShare => ExecuteSetFactoryAllocationShare(command, session, teamId),
                BotCommandKind.PostNeed => ExecutePostNeed(command, session, teamId),
                BotCommandKind.WithdrawNeed => ExecuteWithdrawNeed(command, session, teamId),
                BotCommandKind.EmergencyPurchase => ExecuteEmergencyPurchase(command, session, teamId),
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

    private static BotCommandExecutionResult ExecuteTakeLoan(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.Amount is not { } amount)
        {
            return new BotCommandExecutionResult.DomainError("TakeLoan requires amount.");
        }

        return new BotCommandExecutionResult.Success(session.TakeLoan(teamId, amount));
    }

    private static BotCommandExecutionResult ExecuteRepayLoan(BotCommand command, GameSession session, Ulid teamId)
    {
        if (command.Amount is not { } amount)
        {
            return new BotCommandExecutionResult.DomainError("RepayLoan requires amount.");
        }

        return new BotCommandExecutionResult.Success(session.RepayLoan(teamId, amount));
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
