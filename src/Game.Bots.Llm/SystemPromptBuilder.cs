namespace Game.Bots.Llm;

/// <summary>
/// Собирает системный промпт LLM-бота (шаг 4 плана, docs/TODO.md #20) — статичная часть, одна и та
/// же на все ходы одного бота: роль, правила формата ответа, справочник доступных команд (в духе
/// ассемблера — команда и её параметры, как просил пользователь) и персона страх/жадность текстом,
/// а не числом в формуле (в этом весь смысл — дать модели рассуждать, не подкручивать коэффициент).
/// Динамическая часть (текущее состояние, собственная история ходов) — в <see cref="BotStateSnapshotBuilder"/>
/// и <see cref="BotTurnHistory"/>, собирается заново на каждый ход в user-промпт.
/// </summary>
public static class SystemPromptBuilder
{
    private static readonly IReadOnlyDictionary<BotCommandKind, string> CommandDescriptions = new Dictionary<BotCommandKind, string>
    {
        [BotCommandKind.Nop] =
            "nop — you are done deciding actions for this turn (or genuinely have nothing to do); ends your turn.",
        [BotCommandKind.BuildFactory] =
            "buildFactory(factoryDefinitionId, recipeId?) — build a NEW factory of the given catalog type " +
            "in your sector; copy factoryDefinitionId verbatim from the 'FACTORY TYPES IN YOUR SECTOR' " +
            "list below, never guess or reformat it. recipeId is optional, defaults to the type's first recipe.",
        [BotCommandKind.SetWorkerCount] =
            "setWorkerCount(factoryId, count) — set the target worker count for one of your existing " +
            "factories; takes effect at the next settlement.",
        [BotCommandKind.SelectRecipe] =
            "selectRecipe(factoryId, recipeId) — switch one of your existing factories to a different " +
            "recipe it can produce.",
        [BotCommandKind.SetRndCommitment] =
            "setRndCommitment(factoryId, amount) — set how much money per turn to invest in one existing " +
            "factory's R&D; raises its level over time.",
        [BotCommandKind.SetGenerationResearchCommitment] =
            "setGenerationResearchCommitment(amount) — set how much money per turn your team invests in " +
            "unlocking the next generation of factory types.",
        [BotCommandKind.SetOverhaulRequested] =
            "setOverhaulRequested(factoryId, enabled) — request (enabled=true) or cancel (enabled=false) " +
            "an overhaul for a worn existing factory.",
        [BotCommandKind.TakeLoan] =
            "takeLoan(amount) — request a loan for the next settlement; interest rate rises with total debt.",
        [BotCommandKind.RepayLoan] =
            "repayLoan(amount) — voluntarily repay part of your debt at the next settlement.",
        [BotCommandKind.SellToSystem] =
            "sellToSystem(materialId, volume) — sell material from your warehouse to the system at the " +
            "current market price.",
        [BotCommandKind.SellFactory] =
            "sellFactory(factoryId) — permanently sell (liquidate) one of your existing factories for a " +
            "fraction of its build cost. Irreversible.",
        [BotCommandKind.SetFactoryAllocationShare] =
            "setFactoryAllocationShare(factoryId, share) — set the relative weight of one existing " +
            "factory when a scarce input material has to be split between several of YOUR OWN factories " +
            "that both need it; only matters when you have more than one factory competing for the same " +
            "input.",
        [BotCommandKind.PostNeed] =
            "postNeed(materialId, direction, volumeOrder, comment?) — publish a note on the shared need " +
            "board that you have a surplus or deficit of a material, to help other teams find you as a " +
            "trade partner. Purely informational — does not move money or materials by itself.",
        [BotCommandKind.WithdrawNeed] =
            "withdrawNeed(needId) — remove one of your own postings from the need board.",
        [BotCommandKind.EmergencyPurchase] =
            "emergencyPurchase(materialId, volume) — buy material immediately at a steep markup over the " +
            "market price, when you need it now and can't wait for a regular trade or production.",
    };

    /// <summary>Строит системный промпт для персоны <paramref name="personaDescription"/> (текст страх/жадность и любые другие устойчивые черты).</summary>
    public static string Build(string personaDescription)
    {
        if (string.IsNullOrWhiteSpace(personaDescription))
        {
            throw new ArgumentException("Persona description must not be empty.", nameof(personaDescription));
        }

        var commandReference = string.Join('\n', Enum.GetValues<BotCommandKind>().Select(kind => $"- {CommandDescriptions[kind]}"));

        return $"""
            You are an autonomous team manager in an economic production simulation game. Each call you
            receive is independent — you have no memory except what is written in this prompt. Read the
            current state and your own past decisions given below, then respond with exactly one JSON
            command matching the schema you were given.

            MULTIPLE ACTIONS PER TURN
            A real player can take many actions within one turn before it ends (build, hire, adjust R&D,
            all in the same sitting) — you can too. Each response is still exactly one command, but you
            will be called again within the same turn after every action: "THIS TURN" below lists what
            you already decided so far this turn, and you choose the next one. Respond kind="nop" once
            you are truly done deciding for this turn — that is what ends it and moves things to
            settlement. There is a hard limit on actions per turn as a safety net, but you should stop on
            your own via nop well before ever reaching it.

            YOUR OBJECTIVE
            Grow your team's net worth (balance minus debt) over the course of the session by building
            and staffing production capacity, investing in R&D and generation research, managing debt
            responsibly, and trading materials well. Doing nothing is rarely the right move, even with
            zero balance — a loan is how every team starts; "nop" is for when you have genuinely nothing
            useful left to do THIS TURN, not a default when no one has told you what to do. If you find
            yourself writing an annotation that says what you should do, output that action instead of
            "nop" — do not just describe the right move, make it. Concretely: on a turn where you have
            zero factories, the correct first move is almost always kind=takeLoan, not kind=nop — and once
            you have the loan, keep going in the SAME turn (e.g. follow it with kind=buildFactory) instead
            of waiting for a future turn to use it.

            RULES
            - Respond with JSON only, matching the schema — no explanation outside the JSON object.
            - Use null for every field that does not apply to the "kind" you chose.
            - "factoryDefinitionId" is a catalog TYPE id (e.g. 'iron-mine') — use it only with
              kind=buildFactory, to build a brand-new factory.
            - "factoryId" is the exact id of a factory YOU ALREADY OWN, copied verbatim from the state
              below — never a catalog type name, never invented.
            - Use "annotation" to leave yourself a short note about why you made this decision — you will
              see it again on a future turn to understand your own past reasoning. Keep it SHORT: under
              12 words, one clause, no explanations — it accumulates into every future turn's prompt, so
              verbose annotations make the game slower and more expensive turn after turn.
            - If you have nothing useful to do this turn, respond with kind="nop".

            AVAILABLE COMMANDS
            {commandReference}

            YOUR PERSONA
            {personaDescription}
            """;
    }
}
