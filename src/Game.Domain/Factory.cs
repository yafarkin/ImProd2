namespace Game.Domain;

/// <summary>
/// Фабрика команды, построенная по <see cref="FactoryDefinition"/>. Держит рабочих, выбранный
/// продукт (рецепт), уровень и накопленные вложения в R&amp;D.
/// </summary>
public sealed class Factory
{
    /// <summary>Уникальный идентификатор фабрики, сгенерированный при её постройке.</summary>
    public Ulid Id { get; }

    /// <summary>Тип фабрики (какие рецепты доступны, к какому сектору она относится).</summary>
    public FactoryDefinition Definition { get; }

    /// <summary>Число рабочих на фабрике сейчас; никогда не отрицательно.</summary>
    public int Workers { get; private set; }

    /// <summary>
    /// Сколько рабочих команда хочет видеть на этой фабрике по итогам ближайшего расчёта тика (SPEC
    /// §5.6) — объявляется свободно и бесплатно в течение хода (см. <see
    /// cref="Game.Engine.GameSession.SetWorkerCount"/>), сколько угодно раз, ни на что не влияя до
    /// расчёта. Реальные наём/увольнение до этого значения и разовая плата за них — на фазе расчёта
    /// (см. <see cref="Game.Engine.WorkforceStep"/>), один раз, по итоговой разнице с <see
    /// cref="Workers"/> на момент расчёта — тот же приём, что и у <see cref="RndCommitmentPerTurn"/>.
    /// Сразу после реального найма/увольнения (<see cref="Hire"/>/<see cref="Fire"/>) всегда равно
    /// <see cref="Workers"/> — расхождение бывает только пока команда ещё не досчитала ход.
    /// </summary>
    public int DesiredWorkers { get; private set; }

    /// <summary>Рецепт, выбранный для производства сейчас; всегда один из <see cref="FactoryDefinition.Recipes"/>.</summary>
    public Recipe SelectedRecipe { get; private set; }

    /// <summary>Текущий уровень фабрики (открывается через R&amp;D).</summary>
    public int Level { get; private set; }

    /// <summary>Накопленные вложения в R&amp;D этой фабрики.</summary>
    public decimal RndInvestment { get; private set; }

    /// <summary>
    /// Относительная доля этой фабрики при разборе дефицитного сырья, общего с другими фабриками
    /// той же команды (несколько экземпляров одного типа или просто конкурирующие рецепты) — не
    /// процент, обязанный суммироваться до 100, а вес: 60 и 40 делят дефицит 60/40, как и 6 и 4.
    /// Ни на что не влияет, пока материала хватает всем претендентам — только когда его на всех не
    /// хватает.
    /// </summary>
    public decimal AllocationShare { get; private set; } = 1m;

    /// <summary>
    /// Сколько команда выделяет на R&amp;D этой фабрики за ход — списывается автоматически каждый
    /// ход (см. <see cref="Game.Engine.TickFinanceStep"/>), а не разово, до тех пор пока команда не
    /// поменяет значение (запрос пользователя: «постоянные затраты», не разовое вложение). 0 по
    /// умолчанию — не значит «на паузе», значит «пока не выделяли».
    /// </summary>
    public decimal RndCommitmentPerTurn { get; private set; }

    public Factory(Ulid id, Sector ownerSector, FactoryDefinition definition, Recipe? selectedRecipe = null)
    {
        if (id == Ulid.Empty)
        {
            throw new ArgumentException("Factory id must not be empty.", nameof(id));
        }
        ArgumentNullException.ThrowIfNull(ownerSector);
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Sector != ownerSector)
        {
            throw new ArgumentException(
                $"Team sector '{ownerSector.Id}' does not match factory definition sector '{definition.Sector.Id}'.",
                nameof(definition));
        }

        selectedRecipe ??= definition.Recipes[0];
        if (!definition.Recipes.Contains(selectedRecipe))
        {
            throw new ArgumentException(
                $"Recipe '{selectedRecipe.Id}' is not produced by factory definition '{definition.Id}'.",
                nameof(selectedRecipe));
        }

        Id = id;
        Definition = definition;
        SelectedRecipe = selectedRecipe;
        Workers = 0;
        DesiredWorkers = 0;
        Level = 1;
        RndInvestment = 0m;
    }

    /// <summary>
    /// Нанимает указанное число рабочих прямо сейчас. Заодно подтягивает <see cref="DesiredWorkers"/>
    /// до нового <see cref="Workers"/> — реальный наём всегда закрывает объявленное расхождение
    /// целиком (см. <see cref="Game.Engine.WorkforceStep"/>), поэтому сразу после него расхождения
    /// снова нет.
    /// </summary>
    public void Hire(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Hire count must be positive.");
        }

        Workers += count;
        DesiredWorkers = Workers;
    }

    /// <summary>
    /// Увольняет указанное число рабочих прямо сейчас; бросает исключение, если их больше, чем есть на
    /// фабрике. Заодно подтягивает <see cref="DesiredWorkers"/> до нового <see cref="Workers"/> — см.
    /// doc-comment <see cref="Hire"/>.
    /// </summary>
    public void Fire(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Fire count must be positive.");
        }
        if (count > Workers)
        {
            throw new InvalidOperationException($"Cannot fire {count} workers, factory '{Id}' only has {Workers}.");
        }

        Workers -= count;
        DesiredWorkers = Workers;
    }

    /// <summary>Переключает фабрику на другой рецепт из числа доступных её типу.</summary>
    public void SelectRecipe(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (!Definition.Recipes.Contains(recipe))
        {
            throw new ArgumentException(
                $"Recipe '{recipe.Id}' is not produced by factory definition '{Definition.Id}'.", nameof(recipe));
        }

        SelectedRecipe = recipe;
    }

    /// <summary>Меняет долю фабрики при разборе дефицитного сырья (см. <see cref="AllocationShare"/>). Ноль допустим — не участвует в разборе дефицита, отрицательное значение смысла не имеет.</summary>
    public void SetAllocationShare(decimal share)
    {
        if (share < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(share), share, "Allocation share must not be negative.");
        }

        AllocationShare = share;
    }

    /// <summary>
    /// Меняет объявленную численность рабочих на ближайший расчёт (см. <see cref="DesiredWorkers"/>).
    /// Ноль допустим — «сократить весь штат до нуля». Не трогает <see cref="Workers"/> — реальный
    /// наём/увольнение происходит отдельно, на фазе расчёта.
    /// </summary>
    public void SetDesiredWorkers(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Desired worker count must not be negative.");
        }

        DesiredWorkers = count;
    }

    /// <summary>
    /// Меняет сумму, выделяемую на R&amp;D этой фабрики за ход (см. <see cref="RndCommitmentPerTurn"/>).
    /// Потолок суммы конфигурируется отдельно (<see cref="Game.Config.Economy.RndConfig.MaxCommitmentPerTurn"/>)
    /// и проверяется на уровне <see cref="Game.Engine.GameSession"/>, а не здесь — фабрика сама по
    /// себе не знает конфиг сессии, как и везде в этом классе (см. <see cref="SetAllocationShare"/>).
    /// </summary>
    public void SetRndCommitment(decimal amountPerTurn)
    {
        if (amountPerTurn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountPerTurn), amountPerTurn, "R&D commitment must not be negative.");
        }

        RndCommitmentPerTurn = amountPerTurn;
    }

    /// <summary>Добавляет вложение в R&amp;D этой фабрики.</summary>
    public void InvestInRnd(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "R&D investment must be positive.");
        }

        RndInvestment += amount;
    }

    /// <summary>Повышает уровень фабрики на единицу.</summary>
    public void AdvanceLevel()
    {
        Level++;
    }
}
