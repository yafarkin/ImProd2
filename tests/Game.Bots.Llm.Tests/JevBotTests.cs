using System.Net;
using System.Text;
using System.Text.Json;
using Game.Bots.Llm.Jev;
using Game.Engine;

namespace Game.Bots.Llm.Tests;

public sealed class JevBotTests
{
    [Fact]
    public void Catalogue_IsReadOnly_AndEveryInitialCandidateExecutes()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var count = session.Entries.Count;
        var actions = new JevActionCatalog(new()).Build(session, teamId);
        Assert.Equal(count, session.Entries.Count);
        Assert.Contains(actions, a => a.Command.Kind == BotCommandKind.BuildFactory);
        Assert.DoesNotContain(actions, a => a.Command.FactoryDefinitionId == "microchip-factory");
        foreach (var action in actions)
        {
            var (copy, id) = TestSession.StartSingleTeamSession();
            Assert.IsNotType<BotCommandExecutionResult.DomainError>(new BotCommandExecutor().Execute(action.Command, copy, id, new Random(42)));
        }
    }

    [Fact]
    public async Task RebuildsCandidatesAfterBuild_ExecutesThroughJournal_AndBoundsCalls()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var records = new List<JevDecisionRecord>();
        var calls = 0;
        var client = new FakeClient(request =>
        {
            calls++;
            var key = calls == 1 ? request.Choices.Keys.First(k => k.StartsWith("build:iron-mine:", StringComparison.Ordinal))
                : request.Choices.Keys.First(k => k.StartsWith("workers:", StringComparison.Ordinal));
            return Task.FromResult(new JevAnswer(key, "{}"));
        });
        await new JevBot(client, new() { MaxActionsPerTurn = 2 }).TakeTurnAsync(session, teamId, new Random(42), r => { records.Add(r); return Task.CompletedTask; });
        Assert.Equal(2, calls);
        var factory = Assert.Single(session.State.Teams[teamId].Factories);
        Assert.True(factory.DesiredWorkers > 0);
        Assert.All(records, r => Assert.Equal("success", r.Outcome));
        Assert.Contains(session.Entries, e => e.Change is FactoryBuilt);
        Assert.Contains(session.Entries, e => e.Change is WorkerCountSet);
    }

    [Fact]
    public async Task AControlCannotBeChangedTwiceInOneTurn()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.BuildFactory(teamId, "iron-mine");
        var calls = 0;
        var client = new FakeClient(request =>
        {
            calls++;
            if (calls == 1) return Task.FromResult(new JevAnswer(request.Choices.Keys.First(k => k.StartsWith("workers:", StringComparison.Ordinal)), "{}"));
            Assert.DoesNotContain(request.Choices.Keys, k => k.StartsWith("workers:", StringComparison.Ordinal));
            return Task.FromResult(new JevAnswer("finish", "{}"));
        });
        await new JevBot(client, new()).TakeTurnAsync(session, teamId, new Random(42), _ => Task.CompletedTask);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("unknown")]
    public async Task FailureFinishesTurnAndLogsFallback(string kind)
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var entries = session.Entries.Count;
        var records = new List<JevDecisionRecord>();
        var client = new FakeClient(_ => kind switch
        {
            "network" => throw new HttpRequestException("offline"),
            "timeout" => throw new TaskCanceledException("timeout"),
            _ => Task.FromResult(new JevAnswer("not-an-action", "raw invalid choice")),
        });
        await new JevBot(client, new()).TakeTurnAsync(session, teamId, new Random(42), r => { records.Add(r); return Task.CompletedTask; });
        Assert.Equal(entries, session.Entries.Count);
        Assert.StartsWith("fallback:", Assert.Single(records).Outcome);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        using var cancellation = new CancellationTokenSource();
        var client = new FakeClient(_ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JevBot(client, new()).TakeTurnAsync(session, teamId, new Random(42), _ => Task.CompletedTask, cancellation.Token));
    }

    [Fact]
    public void CatalogueReflectsGeneration_Workers_Repair_AndPhase()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.BuildFactory(teamId, "iron-mine");
        var catalog = new JevActionCatalog(new());
        var actions = catalog.Build(session, teamId);
        Assert.DoesNotContain(actions, a => a.Command.Kind == BotCommandKind.SetOverhaulRequested);
        Assert.All(actions.Where(a => a.Command.Kind == BotCommandKind.BuildFactory), a =>
            Assert.True(session.State.Config.FactoryDefinitions.Single(d => d.Id == a.Command.FactoryDefinitionId).Recipes[0].Output.Level <= session.State.Teams[teamId].UnlockedGeneration));
        TestSession.SettleOneTurn(session, new Random(42));
        var updated = catalog.Build(session, teamId);
        Assert.NotEmpty(updated);
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        Assert.Throws<InvalidOperationException>(() => catalog.Build(session, teamId));
    }

    [Theory]
    [InlineData("{\"answers\":{\"action\":{\"choice\":\"finish\"}}}", true)]
    [InlineData("{\"answers\":{\"action\":{\"choice\":\"invented\"}}}", false)]
    [InlineData("{\"answers\":{}}", false)]
    public async Task ClientUsesDecisionProtocolAndValidatesSelection(string response, bool valid)
    {
        var handler = new StubHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var client = new JevClient(http, "nimble");
        var request = new JevRequest(new { balance = 10 }, new Dictionary<string, string> { ["finish"] = "Finish turn" });
        if (valid) Assert.Equal("finish", (await client.ChooseAsync(request, default)).Choice);
        else await Assert.ThrowsAsync<JevResponseException>(() => client.ChooseAsync(request, default));
        Assert.Equal("/v1/systemone", handler.Path);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("choice", body.RootElement.GetProperty("questions").GetProperty("action").GetProperty("type").GetString());
        Assert.Equal("nimble", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task FullTrainingSession_WithScriptedChoices_ReplaysWithoutModel()
    {
        var config = Game.Config.Loading.GameConfigLoader.LoadFromFiles(
            Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "training-1-sector.json"),
            Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(config, 98,
            [new TeamSpec { Id = teamId, Name = "Scripted", SectorId = config.Sectors[0].Id }]);
        var workerSuffix = ":" + config.Raw.WorkerProductivity.BaseWorkerCount;
        var records = new List<JevDecisionRecord>();
        var client = new FakeClient(request =>
        {
            var keys = request.Choices.Keys;
            var choice = keys.FirstOrDefault(k => k.StartsWith("build:", StringComparison.Ordinal))
                ?? keys.FirstOrDefault(k => k.StartsWith("workers:", StringComparison.Ordinal) && k.EndsWith(workerSuffix, StringComparison.Ordinal))
                ?? keys.LastOrDefault(k => k.StartsWith("research:", StringComparison.Ordinal) && k != "research:0")
                ?? keys.FirstOrDefault(k => k.StartsWith("sell:", StringComparison.Ordinal))
                ?? "finish";
            return Task.FromResult(new JevAnswer(choice, "scripted"));
        });
        var bot = new JevBot(client, new() { MaxFactoriesPerRecipe = 1, ResearchFractions = [0, 1], UseAnalytics = true });
        var random = new Random(42);
        while (!session.State.IsFinished)
        {
            if (session.State.CurrentPhase == TurnPhase.Settlement)
                session.RunTick(random);
            else
                Assert.False(await bot.TakeTurnAsync(session, teamId, random, r => { records.Add(r); return Task.CompletedTask; }));
            session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        }
        Assert.DoesNotContain(records, r => r.Outcome.StartsWith("domain-error:", StringComparison.Ordinal));
        Assert.Contains(session.State.Teams[teamId].Factories, f => f.SelectedRecipe.Output.Id == "steel-structure");
        var replay = new GameSessionState(config);
        foreach (var entry in session.Entries) entry.Change.Apply(replay);
        Assert.Equal(session.State.Teams[teamId].Balance, replay.Teams[teamId].Balance);
        Assert.Equal(session.State.Teams[teamId].Factories.Count, replay.Teams[teamId].Factories.Count);
        Assert.Equal(session.State.IsFinished, replay.IsFinished);
    }

    [Fact]
    public async Task HttpErrorBodyIsPreservedInDecisionLog()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        using var http = new HttpClient(new StubHandler("{\"error\":\"update model\"}", HttpStatusCode.BadRequest))
            { BaseAddress = new Uri("http://localhost:11434/") };
        var records = new List<JevDecisionRecord>();
        Assert.True(await new JevBot(new JevClient(http, "nimble"), new()).TakeTurnAsync(session, teamId, new Random(42),
            r => { records.Add(r); return Task.CompletedTask; }));
        Assert.Contains("update model", Assert.Single(records).RawResponse);
        Assert.Contains("400", records[0].Outcome);
    }

    private sealed class FakeClient(Func<JevRequest, Task<JevAnswer>> choose) : IJevClient
    {
        public Task<JevPlan> AssessAsync(JevPlanningRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JevAnswer> ChooseAsync(JevRequest request, CancellationToken cancellationToken) => choose(request);
    }

    private sealed class StubHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
