using Game.Bots.Llm;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

namespace Game.Bots.Llm.Tests;

/// <summary>
/// Проверка механизма возобновления остановленного файлового опыта (docs/ASSISTANT-BOT-NEXT-STEP.md):
/// одобренный ответ находится и исполняется ровно один раз, размещение/хеш/конверт охраняются,
/// случайные команды отвергаются. Журналы настоящие (DurableEventLog) — как в остановленном опыте.
/// </summary>
public sealed class FileBotResumeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"filebot-resume-{Guid.NewGuid():N}");

    public FileBotResumeTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public void ApprovedResponseIsMissing_ReturnsNull_NotAnError()
    {
        var (session, id) = TestSession.StartSingleTeamSession();
        WriteRequest(session.State.CurrentTurn);
        Assert.Null(FileBotResume.FindApproved(_directory, session.State.CurrentTurn));
    }

    [Fact]
    public void NoRequestAtAll_ReturnsNull()
    {
        Assert.Null(FileBotResume.FindApproved(_directory, 1));
    }

    [Fact]
    public void ApprovedResponse_IsFound_AndExecutable_ThroughJournal()
    {
        var (session, id) = TestSession.StartSingleTeamSession();
        var turn = session.State.CurrentTurn;
        WriteRequest(turn);
        var response = $$"""{"requestId":"turn-{{turn}}","actions":[{"kind":"sellToSystem","materialId":"ore","volume":5}]}""";
        WriteApproved(turn, response);

        var pending = FileBotResume.FindApproved(_directory, turn);
        Assert.NotNull(pending);

        var entriesBefore = session.Entries.Count;
        var results = FileBotProtocol.Execute(pending.ResponseJson, $"turn-{turn}", session, id,
            new Dictionary<string, Ulid>(), new Random(42), 12);
        var entriesAfter = session.Entries.Count;

        Assert.Equal(["ok:SellToSystem"], results);
        // Заявка записывается в журнал событием; склад урезается при ближайшем расчёте тика.
        Assert.Contains(session.Entries, e => e.Change is MaterialSaleRequested);
        Assert.Equal(entriesBefore + 1, entriesAfter);
    }

    [Fact]
    public void TamperedResponseBytes_AreRejected()
    {
        var (session, id) = TestSession.StartSingleTeamSession();
        var turn = session.State.CurrentTurn;
        WriteRequest(turn);
        WriteApproved(turn, """{"requestId":"turn-0","actions":[]}""");
        Assert.Throws<InvalidOperationException>(() => FileBotResume.FindApproved(_directory, turn));
    }

    [Fact]
    public void WrongRequestId_IsRejected()
    {
        var (session, id) = TestSession.StartSingleTeamSession();
        var turn = session.State.CurrentTurn;
        WriteRequest(turn);
        var response = $$"""{"requestId":"turn-999","actions":[]}""";
        WriteApproved(turn, response);
        Assert.Throws<InvalidOperationException>(() => FileBotResume.FindApproved(_directory, turn));
    }

    [Fact]
    public void RandomTradeCommand_IsRejectedBeforeExecution()
    {
        var (session, id) = TestSession.StartSingleTeamSession();
        var turn = session.State.CurrentTurn;
        WriteRequest(turn);
        var response = """{"requestId":"turn-1","actions":[{"kind":"fulfillTradeOffer","offerId":"o1"}]}""";
        WriteApproved(turn, response);
        Assert.Throws<InvalidOperationException>(() => FileBotResume.FindApproved(_directory, turn));
        // Отвержение — до исполнения: журнал не меняется.
        Assert.DoesNotContain(session.Entries, e => e.Change is MaterialSaleRequested);
    }

    /// <summary>
    /// Сквозная проверка на настоящем durable-журнале — тот же слой восстановления, что и в
    /// остановленном опыте: исходная сессия пишет журнал, «перезапуск» доигрывает его, находит
    /// одобренный ответ и исполняет в фазе решений. Исполнение — событие в журнале новой обёртки;
    /// состояние исходной (остановленной) сессии не трогается.
    /// </summary>
    [Fact]
    public void StoppedRun_ReplaysJournal_AndExecutesPendingAnswer()
    {
        var config = TestSession.LoadConfig();
        var teamId = Ulid.NewUlid();
        var journalPath = Path.Combine(_directory, "journal.jsonl");
        var snapshotPath = Path.Combine(_directory, "snapshot.json");
        var original = DurableEventLog<GameSessionState>.Open(journalPath, snapshotPath,
            () => new GameSessionState(config));
        var originalSession = GameSession.StartWithEndTurn(original, 20, [new TeamSpec { Id = teamId, Name = "T", SectorId = "A" }]);
        originalSession.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        var turn = originalSession.State.CurrentTurn;
        WriteRequest(turn);
        var response = $$"""{"requestId":"turn-{{turn}}","actions":[{"kind":"sellToSystem","materialId":"ore","volume":5}]}""";
        WriteApproved(turn, response);
        var originalEntries = originalSession.Entries.Count;

        var resumed = DurableEventLog<GameSessionState>.Open(journalPath, snapshotPath,
            () => new GameSessionState(config));
        Assert.Equal(originalEntries, resumed.Entries.Count);
        Assert.True(resumed.VerifyIntegrity());
        var resumedSession = new GameSession(resumed);
        Assert.Equal(turn, resumedSession.State.CurrentTurn);
        Assert.Equal(TurnPhase.Decision, resumedSession.State.CurrentPhase);
        Assert.DoesNotContain(resumedSession.State.Teams[teamId].Warehouse.Stock,
            s => s.Material.Id == "ore" && s.Quantity > 0);

        var pending = FileBotResume.FindApproved(_directory, resumedSession.State.CurrentTurn);
        Assert.NotNull(pending);
        var results = FileBotProtocol.Execute(pending.ResponseJson, $"turn-{turn}", resumedSession, teamId,
            new Dictionary<string, Ulid>(), new Random(42), 12);
        Assert.Equal(["ok:SellToSystem"], results);
        // Заявка продана системе: она исполнится при ближайшем расчёте, текущий склад не меняется.
        Assert.Equal(originalEntries + 1, resumed.Entries.Count);
        Assert.True(resumed.VerifyIntegrity());
        // Остановленная (исходная) обёртка не изменилась: возобновление не переписывает историю.
        Assert.Equal(originalEntries, originalSession.Entries.Count);
    }

    /// <summary>Запрос на текущий ход — содержимое неважно, проверяется только наличие файла.</summary>
    private void WriteRequest(int turn)
    {
        var stem = Path.Combine(_directory, $"{turn:000}");
        File.WriteAllText($"{stem}.request.json", """{"requestId":"turn-1","turn":1,"stock":[]}""");
    }

    /// <summary>Ответ и одобрение счётчика — тот же маркер SHA-256 точных байтов, что в живом цикле.</summary>
    private void WriteApproved(int turn, string response)
    {
        var stem = Path.Combine(_directory, $"{turn:000}");
        File.WriteAllText($"{stem}.response.json", response);
        File.WriteAllText($"{stem}.approved",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(response))));
    }
}
