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
    /// Сумма, которую команда объявила занять на ближайшем расчёте (SPEC §4, §5.9: решения не
    /// применяются сразу — только на расчёте) — само объявление бесплатно и мгновенно, реальное
    /// зачисление и рост долга происходят один раз, на расчёте (<see cref="Game.Engine.VoluntaryLoanStep"/>).
    /// Последнее объявление в пределах хода замещает предыдущее, а не суммируется с ним (тот же
    /// приём, что и у <see cref="Game.Domain.Factory.DesiredWorkers"/>); 0 — заявка снята.
    /// </summary>
    public decimal PendingLoanTakeAmount { get; private set; }

    /// <summary>
    /// Сумма, которую команда объявила добровольно погасить на ближайшем расчёте, сверх
    /// обязательного платежа. Реальный остаток долга на момент расчёта может отличаться от того, что
    /// было видно в момент решения (проценты, обязательный платёж — уже применены тем же расчётом
    /// раньше) — поэтому потолок «нельзя погасить больше реального долга» считается на расчёте, не
    /// здесь (см. <see cref="Game.Engine.VoluntaryLoanStep"/>).
    /// </summary>
    public decimal PendingLoanRepayAmount { get; private set; }

    private readonly Dictionary<string, decimal> _pendingEmergencyPurchaseVolumeByMaterial = new();

    /// <summary>
    /// Заявленные на ближайший расчёт объёмы аварийной закупки, по коду материала (SPEC §4, §5.3:
    /// решения не применяются сразу) — тем же приёмом, что и <see cref="PendingLoanTakeAmount"/>:
    /// последнее объявление по материалу замещает предыдущее (упрощение — команда, желающая купить
    /// несколько раз за ход, теперь просто объявляет итоговый объём один раз; штраф «давления» за
    /// дробление внутри одного хода этим убран, штраф за растягивание закупок по нескольким ходам —
    /// нет, он считается на расчёте по фактической истории, см. <see cref="Game.Engine.EmergencyPurchaseStep"/>).
    /// </summary>
    public IReadOnlyDictionary<string, decimal> PendingEmergencyPurchaseVolumeByMaterial => _pendingEmergencyPurchaseVolumeByMaterial;

    private readonly Dictionary<string, decimal> _pendingSaleVolumeByMaterial = new();

    /// <summary>
    /// Заявленные на ближайший расчёт объёмы продажи системе, по коду материала — симметрично <see
    /// cref="PendingEmergencyPurchaseVolumeByMaterial"/>. Реальный остаток на складе на момент
    /// расчёта может быть меньше заявленного (см. <see cref="Game.Engine.SystemSaleStep"/>) — потолок
    /// считается там же, не здесь.
    /// </summary>
    public IReadOnlyDictionary<string, decimal> PendingSaleVolumeByMaterial => _pendingSaleVolumeByMaterial;

    /// <summary>
    /// Накопленная штрафная надбавка к ставке по кредиту — растёт с каждым принудительным займом
    /// (SPEC §5.9: «ставка принудительного займа заведомо хуже любого добровольного») и применяется
    /// ко всему долгу команды, а не только к принудительно взятой части.
    /// </summary>
    public decimal PenaltyRateSurcharge { get; private set; }

    private readonly List<Factory> _factories = new();

    /// <summary>Фабрики, построенные командой.</summary>
    public IReadOnlyList<Factory> Factories => _factories;

    /// <summary>
    /// Поколение пирамиды сырья (см. <see cref="Material.Level"/>), фабрики которого команда может
    /// строить прямо сейчас — растёт через командное исследование (см.
    /// <see cref="GenerationResearchInvestment"/>), а не доступно сразу целиком (запрос
    /// пользователя: будущие фабрики должны появляться постепенно).
    /// </summary>
    public int UnlockedGeneration { get; private set; }

    /// <summary>Накопленные вложения команды в исследование следующего поколения фабрик.</summary>
    public decimal GenerationResearchInvestment { get; private set; }

    /// <summary>
    /// Сколько команда выделяет на исследование следующего поколения за ход — списывается
    /// автоматически каждый ход (см. <see cref="Game.Engine.TickFinanceStep"/>), тем же приёмом, что
    /// и <see cref="Factory.RndCommitmentPerTurn"/>, только на уровне команды, а не одной фабрики.
    /// </summary>
    public decimal GenerationResearchCommitmentPerTurn { get; private set; }

    public Team(Ulid id, string name, Sector sector, int startingGeneration = 1)
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
        UnlockedGeneration = startingGeneration;
    }

    /// <summary>Строит фабрику заданного типа для команды; тип фабрики обязан быть из сектора команды.</summary>
    // Проверку сектора дублирует и конструктор Factory — это лишь точка входа для команд.
    public Factory BuildFactory(Ulid factoryId, FactoryDefinition definition, Recipe? selectedRecipe = null, int builtAtTurn = 0)
    {
        var factory = new Factory(factoryId, Sector, definition, selectedRecipe, builtAtTurn);
        _factories.Add(factory);
        return factory;
    }

    /// <summary>
    /// Убирает проданную (ликвидированную) фабрику из списка команды — симметрично <see
    /// cref="BuildFactory"/> (см. <see cref="Game.Engine.FactorySold"/>). Рабочие фабрики перестают
    /// числиться вместе с ней, без отдельного события увольнения — то же упрощение, что и у самой
    /// постройки.
    /// </summary>
    public void RemoveFactory(Ulid factoryId)
    {
        var factory = _factories.FirstOrDefault(f => f.Id == factoryId);
        if (factory is null)
        {
            throw new ArgumentException($"Team '{Id}' has no factory '{factoryId}'.", nameof(factoryId));
        }

        _factories.Remove(factory);
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

    /// <summary>
    /// Оформляет заём: зачисляет сумму на баланс и одновременно увеличивает долг на ту же сумму.
    /// Заодно снимает <see cref="PendingLoanTakeAmount"/> — заявка, из которой взята эта сумма,
    /// исполнена (единственный вызывающий код — <see cref="Game.Engine.LoanTaken.Apply"/>, всегда с
    /// уже разрешённой на расчёте суммой).
    /// </summary>
    public void TakeLoan(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Loan amount must be positive.");
        }

        Balance += amount;
        Debt += amount;
        PendingLoanTakeAmount = 0;
    }

    /// <summary>
    /// Объявляет желаемую сумму займа на ближайший расчёт (см. <see cref="PendingLoanTakeAmount"/>).
    /// В отличие от <see cref="TakeLoan"/> ничего не зачисляет — это только намерение.
    /// </summary>
    public void RequestLoan(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Requested loan amount must not be negative.");
        }

        PendingLoanTakeAmount = amount;
    }

    /// <summary>
    /// Уменьшает тело долга на сумму погашения. Баланс эта операция сама не трогает — списание с
    /// баланса делает вызывающее событие отдельным вызовом <see cref="Debit"/> (тот же приём
    /// разделения «стоимость» / «сам факт», что и в <c>FactoryBuilt.Apply</c>, который отдельно
    /// зовёт <c>BuildFactory</c> и <see cref="Debit"/>). Нельзя погасить больше, чем реально должны —
    /// долг не уходит в минус. Используется и обязательным платежом (<see
    /// cref="Game.Engine.MandatoryLoanRepaymentCharged"/>), и добровольным (<see
    /// cref="Game.Engine.LoanRepaid"/>) — поэтому сама не трогает <see
    /// cref="PendingLoanRepayAmount"/>: обязательный платёж случается в начале каждого хода, а
    /// заявка на добровольное погашение разрешается только в конце того же хода (<see
    /// cref="Game.Engine.VoluntaryLoanStep"/>); если бы этот метод её сбрасывал, обязательный платёж
    /// молча стирал бы ещё не рассмотренную заявку. Снятие заявки — забота вызывающей стороны (см.
    /// <see cref="Game.Engine.LoanRepaid.Apply"/>).
    /// </summary>
    public void RepayLoan(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Repayment amount must be positive.");
        }
        if (amount > Debt)
        {
            throw new InvalidOperationException($"Cannot repay {amount}, team '{Id}' only owes {Debt}.");
        }

        Debt -= amount;
    }

    /// <summary>
    /// Объявляет желаемую сумму добровольного погашения на ближайший расчёт (см.
    /// <see cref="PendingLoanRepayAmount"/>). В отличие от <see cref="RepayLoan"/> ничего не
    /// списывает — это только намерение, и, в отличие от него же, не ограничена текущим <see
    /// cref="Debt"/> (реальный остаток долга на момент расчёта может быть другим — см. doc-comment
    /// <see cref="PendingLoanRepayAmount"/>).
    /// </summary>
    public void RequestLoanRepayment(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Requested repayment amount must not be negative.");
        }

        PendingLoanRepayAmount = amount;
    }

    /// <summary>
    /// Снимает заявку на добровольное погашение без самого погашения — расчёт решил, что реально
    /// гасить нечего (например, долг успел обнулиться обязательным платежом раньше в этом же
    /// расчёте, см. <see cref="Game.Engine.VoluntaryLoanStep"/>). Без этого метода заявка осталась бы
    /// висеть и могла бы неожиданно исполниться следующим ходом, если у команды к тому времени
    /// появится новый долг, который она не имела в виду гасить.
    /// </summary>
    public void ClearPendingLoanRepayRequest()
    {
        PendingLoanRepayAmount = 0;
    }

    /// <summary>
    /// Объявляет желаемый объём аварийной закупки материала на ближайший расчёт (см.
    /// <see cref="PendingEmergencyPurchaseVolumeByMaterial"/>). 0 снимает заявку по этому материалу.
    /// </summary>
    public void RequestEmergencyPurchase(string materialId, decimal volume)
    {
        ArgumentException.ThrowIfNullOrEmpty(materialId);
        if (volume < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Requested purchase volume must not be negative.");
        }

        if (volume == 0)
        {
            _pendingEmergencyPurchaseVolumeByMaterial.Remove(materialId);
        }
        else
        {
            _pendingEmergencyPurchaseVolumeByMaterial[materialId] = volume;
        }
    }

    /// <summary>Снимает заявку на аварийную закупку материала — вызывается после её разрешения на расчёте (см. <see cref="Game.Engine.EmergencyPurchased.Apply"/>).</summary>
    public void ClearPendingEmergencyPurchase(string materialId) => _pendingEmergencyPurchaseVolumeByMaterial.Remove(materialId);

    /// <summary>
    /// Объявляет желаемый объём продажи материала системе на ближайший расчёт (см.
    /// <see cref="PendingSaleVolumeByMaterial"/>). 0 снимает заявку по этому материалу.
    /// </summary>
    public void RequestSaleToSystem(string materialId, decimal volume)
    {
        ArgumentException.ThrowIfNullOrEmpty(materialId);
        if (volume < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Requested sale volume must not be negative.");
        }

        if (volume == 0)
        {
            _pendingSaleVolumeByMaterial.Remove(materialId);
        }
        else
        {
            _pendingSaleVolumeByMaterial[materialId] = volume;
        }
    }

    /// <summary>Снимает заявку на продажу материала системе — вызывается после её разрешения на расчёте (см. <see cref="Game.Engine.MaterialSoldToSystem.Apply"/>).</summary>
    public void ClearPendingSaleToSystem(string materialId) => _pendingSaleVolumeByMaterial.Remove(materialId);

    /// <summary>Увеличивает штрафную надбавку к ставке по кредиту (после принудительного займа).</summary>
    public void IncreasePenaltyRateSurcharge(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Penalty rate surcharge increase must be positive.");
        }

        PenaltyRateSurcharge += amount;
    }

    /// <summary>
    /// Меняет сумму, выделяемую на исследование следующего поколения фабрик за ход (см.
    /// <see cref="GenerationResearchCommitmentPerTurn"/>). Потолок суммы конфигурируется отдельно
    /// (<see cref="Game.Config.Economy.GenerationResearchConfig.MaxCommitmentPerTurn"/>) и
    /// проверяется на уровне <see cref="Game.Engine.GameSession"/>, а не здесь — команда сама по себе
    /// не знает конфиг сессии, как и везде в этом классе (см. <see cref="Factory.SetRndCommitment"/>).
    /// </summary>
    public void SetGenerationResearchCommitment(decimal amountPerTurn)
    {
        if (amountPerTurn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountPerTurn), amountPerTurn, "Generation research commitment must not be negative.");
        }

        GenerationResearchCommitmentPerTurn = amountPerTurn;
    }

    /// <summary>Добавляет вложение в исследование следующего поколения фабрик; списывает баланс и копит его же.</summary>
    public void InvestInGenerationResearch(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Generation research investment must be positive.");
        }

        Debit(amount);
        GenerationResearchInvestment += amount;
    }

    /// <summary>Повышает разблокированное поколение на единицу.</summary>
    public void AdvanceGeneration()
    {
        UnlockedGeneration++;
    }
}
