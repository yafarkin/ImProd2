namespace Game.Bots.Llm;

/// <summary>
/// Компактное текстовое описание итога хода — то, что попадает в <see cref="BotTurnHistoryEntry.Summary"/>
/// и в отчёт вызывающей стороны. Нарочно короткое, в духе ассемблерной мнемоники, которую пользователь
/// сам предложил как формат истории («build fab #0; set worker count fab #0 = 20»).
/// </summary>
internal static class BotCommandSummary
{
    /// <summary>Описывает итог одного действия одной строкой.</summary>
    public static string Describe(LlmBotTurnResult result) => result.Outcome switch
    {
        LlmBotTurnOutcome.Nop => "nop",
        LlmBotTurnOutcome.Exhausted => "(no valid response — retries exhausted)",
        LlmBotTurnOutcome.Success => DescribeCommand(result.Command!),
        // Запрос пользователя 2026-08-16 (один вызов LLM на весь ход): доменные ошибки и
        // анти-залипательные guard'ы больше не запускают повторный запрос к модели с исправлением —
        // причина пропуска попадает сюда и дальше в кросс-ходовую историю бота (см. doc-comment
        // LlmBotTurnResult.ForSkipped), единственный способ модели узнать об этом на будущем ходу.
        LlmBotTurnOutcome.Skipped => $"(skipped: {DescribeCommand(result.Command!)} — {result.SkipReason})",
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
        BotCommandKind.SellFactory => $"sellFactory({command.FactoryId})",
        BotCommandKind.SetFactoryAllocationShare => $"setFactoryAllocationShare({command.FactoryId}, {command.Share})",
        BotCommandKind.PostNeed => $"postNeed({command.MaterialId}, {command.Direction}, {command.VolumeOrder})",
        BotCommandKind.WithdrawNeed => $"withdrawNeed({command.NeedId})",
        BotCommandKind.EmergencyPurchase => $"emergencyPurchase({command.MaterialId}, {command.Volume})",
        BotCommandKind.PostSellOffer => $"postSellOffer({command.MaterialId}, {command.Volume}, {command.MinPrice}-{command.MaxPrice})",
        BotCommandKind.PostBuyOffer => $"postBuyOffer({command.MaterialId}, {command.Volume}, {command.MinPrice}-{command.MaxPrice})",
        BotCommandKind.WithdrawTradeOffer => $"withdrawTradeOffer({command.TradeOfferId})",
        BotCommandKind.FulfillTradeOffer => $"fulfillTradeOffer({command.TradeOfferId}, {command.Volume} @ {command.UnitPrice})",
        BotCommandKind.Nop => "nop",
        _ => command.Kind.ToString(),
    };
}
