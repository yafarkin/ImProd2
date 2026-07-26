namespace Game.Domain;

/// <summary>
/// Команда игроков: держит свой сектор, склад и построенные фабрики. Аналог Customer из старого
/// прототипа (см. AGENTS §5 — терминология).
/// </summary>
public sealed class Team
{
    /// <summary>Уникальный идентификатор команды, сгенерированный при её создании.</summary>
    public Ulid Id { get; }

    /// <summary>Отображаемое имя команды.</summary>
    public string Name { get; }

    /// <summary>Сектор, в котором работает команда; фабрики можно строить только этого сектора.</summary>
    public Sector Sector { get; }

    /// <summary>Склад команды.</summary>
    public Warehouse Warehouse { get; }

    /// <summary>
    /// Денежный остаток команды. В отличие от склада может уходить в минус — именно это и есть
    /// сигнал для принудительного кредита (SPEC §5.9), а не отдельная проверка сверху.
    /// </summary>
    public decimal Balance { get; private set; }

    /// <summary>Непогашенная сумма долга (стартовый кредит + любые последующие займы, включая принудительные).</summary>
    public decimal Debt { get; private set; }

    /// <summary>
    /// Накопленная штрафная надбавка к ставке по кредиту — растёт с каждым принудительным займом
    /// (SPEC §5.9: «ставка принудительного займа заведомо хуже любого добровольного») и применяется
    /// ко всему долгу команды, а не только к принудительно взятой части.
    /// </summary>
    public decimal PenaltyRateSurcharge { get; private set; }

    private readonly List<Factory> _factories = new();

    /// <summary>Фабрики, построенные командой.</summary>
    public IReadOnlyList<Factory> Factories => _factories;

    public Team(Ulid id, string name, Sector sector)
    {
        if (id == Ulid.Empty)
        {
            throw new ArgumentException("Team id must not be empty.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Team name must not be empty.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(sector);

        Id = id;
        Name = name;
        Sector = sector;
        Warehouse = new Warehouse();
    }

    /// <summary>Строит фабрику заданного типа для команды; тип фабрики обязан быть из сектора команды.</summary>
    // Проверку сектора дублирует и конструктор Factory — это лишь точка входа для команд.
    public Factory BuildFactory(Ulid factoryId, FactoryDefinition definition, Recipe? selectedRecipe = null)
    {
        var factory = new Factory(factoryId, Sector, definition, selectedRecipe);
        _factories.Add(factory);
        return factory;
    }

    /// <summary>Начисляет деньги на баланс (выручка, полученный заём и т.п.).</summary>
    public void Credit(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Credit amount must be positive.");
        }

        Balance += amount;
    }

    /// <summary>
    /// Списывает деньги с баланса (расход). Баланс может уйти в минус — это ожидаемый сигнал для
    /// принудительного кредита, а не ошибка.
    /// </summary>
    public void Debit(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Debit amount must be positive.");
        }

        Balance -= amount;
    }

    /// <summary>Оформляет заём: зачисляет сумму на баланс и одновременно увеличивает долг на ту же сумму.</summary>
    public void TakeLoan(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Loan amount must be positive.");
        }

        Balance += amount;
        Debt += amount;
    }

    /// <summary>Увеличивает штрафную надбавку к ставке по кредиту (после принудительного займа).</summary>
    public void IncreasePenaltyRateSurcharge(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Penalty rate surcharge increase must be positive.");
        }

        PenaltyRateSurcharge += amount;
    }
}
