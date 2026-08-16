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
            "nop — do nothing this turn.",
        [BotCommandKind.BuildFactory] =
            "buildFactory(factoryDefinitionId, recipeId?) — build a NEW factory of the given catalog type " +
            "in your sector. recipeId is optional, defaults to the type's first recipe.",
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

            YOUR OBJECTIVE
            Grow your team's net worth (balance minus debt) over the course of the session by building
            and staffing production capacity, investing in R&D and generation research, managing debt
            responsibly, and trading materials well. Doing nothing is rarely the right move, even with
            zero balance — a loan is how every team starts; "nop" is for when you have genuinely nothing
            useful to do this turn, not a default when no one has told you what to do. If you find
            yourself writing an annotation that says what you should do, output that action instead of
            "nop" — do not just describe the right move, make it. Concretely: on a turn where you have
            zero factories, the correct move is almost always kind=takeLoan, not kind=nop.

            RULES
            - Respond with JSON only, matching the schema — no explanation outside the JSON object.
            - Use null for every field that does not apply to the "kind" you chose.
            - "factoryDefinitionId" is a catalog TYPE id (e.g. 'iron-mine') — use it only with
              kind=buildFactory, to build a brand-new factory.
            - "factoryId" is the exact id of a factory YOU ALREADY OWN, copied verbatim from the state
              below — never a catalog type name, never invented.
            - Use "annotation" to leave yourself a short note about why you made this decision — you will
              see it again on a future turn to understand your own past reasoning.
            - If you have nothing useful to do this turn, respond with kind="nop".

            AVAILABLE COMMANDS
            {commandReference}

            YOUR PERSONA
            {personaDescription}
            """;
    }
}
