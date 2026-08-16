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
}
