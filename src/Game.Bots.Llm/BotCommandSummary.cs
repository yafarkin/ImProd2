namespace Game.Bots.Llm;

/// <summary>
/// Компактное текстовое описание итога хода — то, что попадает в <see cref="BotTurnHistoryEntry.Summary"/>
/// и в отчёт вызывающей стороны. Нарочно короткое, в духе ассемблерной мнемоники, которую пользователь
/// сам предложил как формат истории («build fab #0; set worker count fab #0 = 20»).
/// </summary>
internal static class BotCommandSummary
{
    /// <summary>Описывает итог <see cref="LlmBotDecisionLoop.RunTurnAsync"/> одной строкой.</summary>
    public static string Describe(LlmBotTurnResult result) => result.Outcome switch
    {
        LlmBotTurnOutcome.Nop => "nop",
        LlmBotTurnOutcome.Exhausted => "(no valid command — retries exhausted)",
        LlmBotTurnOutcome.Success => DescribeCommand(result.Command!),
        _ => result.Outcome.ToString(),
    };

    private static string DescribeCommand(BotCommand command) => command.Kind switch
    {
        BotCommandKind.BuildFactory => command.RecipeId is null
            ? $"buildFactory({command.FactoryDefinitionId})"
            : $"buildFactory({command.FactoryDefinitionId}, recipe={command.RecipeId})",
        BotCommandKind.SetWorkerCount => $"setWorkerCount({command.FactoryId}, {command.Count})",
        BotCommandKind.SelectRecipe => $"selectRecipe({command.FactoryId}, {command.RecipeId})",
        BotCommandKind.SetRndCommitment => $"setRndCommitment({command.FactoryId}, {command.Amount})",
        BotCommandKind.SetGenerationResearchCommitment => $"setGenerationResearchCommitment({command.Amount})",
        BotCommandKind.SetOverhaulRequested => $"setOverhaulRequested({command.FactoryId}, {command.Enabled})",
        BotCommandKind.TakeLoan => $"takeLoan({command.Amount})",
        BotCommandKind.RepayLoan => $"repayLoan({command.Amount})",
        BotCommandKind.SellToSystem => $"sellToSystem({command.MaterialId}, {command.Volume})",
        BotCommandKind.Nop => "nop",
        _ => command.Kind.ToString(),
    };
}
