using System.Net;
using System.Text;
using System.Text.Json;
using Game.Bots.Llm.Jev;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm.Tests;

public sealed class JevPlanningTests
{
    [Fact]
    public void Analytics_ResearchThresholdAndBoundedCandidates_AreReadOnly()
    {
        var (session, id) = Training();
        var count = session.Entries.Count;
        var analysis = new JevAnalytics(new() { MaxDevelopmentCandidates = 100 }).Build(session, id);
        var iron = analysis.Development.Single(d => d.Output == "pig-iron");
        Assert.Equal(729, iron.ResearchInvestmentNeeded);
        Assert.Equal(3, iron.ResearchOptions.Single(r => r.CommitmentPerTurn == 250).TurnsToUnlock);
        Assert.Null(iron.ResearchOptions.Single(r => r.CommitmentPerTurn == 0).TurnsToUnlock);
        Assert.All(iron.Inputs, i => Assert.Equal(i.RequiredPerTurn, i.NominalShortfall));
        Assert.Equal(count, session.Entries.Count);
        Assert.Empty(session.State.Teams[id].Factories);
        Assert.Equal(4, new JevAnalytics(new()).Build(session, id).Development.Count);
        session.SetGenerationResearchCommitment(id, 250);
        var random = new Random(42);
        for (var turn = 0; turn < 3; turn++) TestSession.SettleOneTurn(session, random);
        var updated = new JevAnalytics(new() { MaxDevelopmentCandidates = 100 }).Build(session, id);
        Assert.Equal(0, updated.Development.Single(d => d.Output == "pig-iron").ResearchInvestmentNeeded);
        Assert.All(updated.Development.Single(d => d.Output == "pig-iron").ResearchOptions, r => Assert.Equal(0, r.TurnsToUnlock));
        Assert.NotEmpty(updated.RecentCash);
        Assert.True(updated.RecentCash.Last().ChangeSincePreviousDecision < 0);
    }

    [Fact]
    public void Analytics_RepairsReflectConfig_AndUnprofitablePaybackIsNull()
    {
        var (session, id) = Training();
        session.BuildFactory(id, "iron-mine");
        var random = new Random(42);
        for (var i = 0; i < 12; i++) TestSession.SettleOneTurn(session, random);
        var analysis = new JevAnalytics(new() { MaxDevelopmentCandidates = 100 }).Build(session, id);
        var factory = Assert.Single(analysis.Factories);
        var actual = Assert.Single(session.State.Teams[id].Factories);
        var tier = WearCalculator.SelectTier(actual.Condition, session.State.Config.Raw.Wear.OverhaulTiers)!;
        var buildCost = session.State.Config.Raw.FactoryDefinitions.Single(d => d.Id == "iron-mine").BuildCost;
        Assert.Equal(buildCost * tier.CostFraction, factory.OverhaulCost);
        Assert.Equal(tier.DurationTurns, factory.OverhaulDowntime);
        Assert.True(factory.NextConditionWithoutRepair < factory.Condition);
        Assert.True(factory.SnapshotProfit < 0); // Zero workers still incur fixed upkeep.
        foreach (var d in analysis.Development.Where(d => d.OperatingProfitWithFullSupply <= 0)) Assert.Null(d.SimplePaybackTurns);
    }

    [Fact]
    public async Task PlanningRunsOnce_BeforeActions_AndItsAnswersReachEveryChoice()
    {
        var (session, id) = Training();
        var before = session.Entries.Count;
        var steps = new List<string>();
        var logs = new List<JevPlanningRecord>();
        var actions = new List<JevDecisionRecord>();
        var client = new ClientStub
        {
            Assess = request =>
            {
                steps.Add("assess");
                Assert.Equal(before, session.Entries.Count);
                Assert.Equal(3, request.Questions.Count);
                Assert.Equal("noul", request.Questions["research_now"].Type);
                return Task.FromResult(new JevPlan("develop", "iron-ore-recipe", 0.92, "assessment-raw"));
            },
            Choose = request =>
            {
                steps.Add("choose");
                using var state = JsonDocument.Parse(JsonSerializer.Serialize(request.State));
                Assert.Equal("develop", state.RootElement.GetProperty("earlierAssessment").GetProperty("Priority").GetString());
                Assert.Equal(0.92, state.RootElement.GetProperty("earlierAssessment").GetProperty("ResearchNowProbability").GetDouble());
                var key = steps.Count == 2 ? request.Choices.Keys.First(k => k.StartsWith("build:iron-mine:", StringComparison.Ordinal)) : "finish";
                if (key == "finish")
                    Assert.Single(state.RootElement.GetProperty("observation").GetProperty("analysis").GetProperty("Factories").EnumerateArray());
                return Task.FromResult(new JevAnswer(key, "action-raw"));
            },
        };
        var failed = await new JevBot(client, new() { UseAnalytics = true, UsePlanning = true }).TakeTurnAsync(session, id, new Random(42),
            r => { actions.Add(r); return Task.CompletedTask; }, writePlanningLog: r => { logs.Add(r); return Task.CompletedTask; });
        Assert.False(failed);
        Assert.Equal(new[] { "assess", "choose", "choose" }, steps);
        Assert.Equal("assessment-raw", Assert.Single(logs).RawResponse);
        Assert.Equal(2, actions.Count);
        Assert.Single(session.State.Teams[id].Factories);
    }

    [Fact]
    public async Task AssessmentFailure_IsLogged_DoesNotExecuteCommands_AndPropagatesCancellation()
    {
        var (session, id) = Training();
        var entries = session.Entries.Count;
        var logs = new List<JevPlanningRecord>();
        var client = new ClientStub { Assess = _ => throw new JevResponseException("offline", "server-body") };
        Assert.True(await new JevBot(client, new() { UseAnalytics = true, UsePlanning = true }).TakeTurnAsync(session, id, new Random(42),
            _ => throw new Exception("No action call expected"), writePlanningLog: r => { logs.Add(r); return Task.CompletedTask; }));
        Assert.Equal(entries, session.Entries.Count);
        Assert.Equal("server-body", Assert.Single(logs).RawResponse);
        using var cancellation = new CancellationTokenSource();
        client.Assess = _ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JevBot(client, new() { UseAnalytics = true, UsePlanning = true })
            .TakeTurnAsync(session, id, new Random(42), _ => Task.CompletedTask, cancellation.Token));
    }

    [Theory]
    [InlineData("develop", "none", "0.8", true)]
    [InlineData("unknown", "none", "0.8", false)]
    [InlineData("develop", "unknown", "0.8", false)]
    [InlineData("develop", "none", "1.2", false)]
    [InlineData("develop", "none", "null", false)]
    public async Task ClientValidatesAllAssessmentAnswers(string priority, string target, string probability, bool valid)
    {
        var body = $"{{\"answers\":{{\"priority\":{{\"choice\":\"{priority}\"}},\"target\":{{\"choice\":\"{target}\"}},\"research_now\":{{\"noul\":{probability}}}}}}}";
        var handler = new Handler(body);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var (session, id) = Training();
        var analysis = new JevAnalytics(new()).Build(session, id);
        var request = JevPlanning.Create(analysis, analysis);
        if (valid) Assert.Equal(0.8, (await new JevClient(http, "nimble").AssessAsync(request, default)).ResearchNowProbability);
        else await Assert.ThrowsAsync<JevResponseException>(() => new JevClient(http, "nimble").AssessAsync(request, default));
        using var sent = JsonDocument.Parse(handler.Body!);
        Assert.Equal(3, sent.RootElement.GetProperty("questions").EnumerateObject().Count());
        Assert.False(sent.RootElement.GetProperty("questions").GetProperty("research_now").TryGetProperty("criteria", out _));
    }

    private static (GameSession Session, Ulid TeamId) Training()
    {
        var config = GameConfigLoader.LoadFromFiles(Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "training-1-sector.json"),
            Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));
        var id = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(config, 98, [new TeamSpec { Id = id, Name = "Test", SectorId = "A" }]);
        session.RunTick(new Random(42));
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        return (session, id);
    }
    private sealed class ClientStub : IJevClient
    {
        public Func<JevPlanningRequest, Task<JevPlan>> Assess { get; set; } = _ => throw new NotSupportedException();
        public Func<JevRequest, Task<JevAnswer>> Choose { get; init; } = _ => throw new Exception("Unexpected command request.");
        public Task<JevPlan> AssessAsync(JevPlanningRequest request, CancellationToken cancellationToken) => Assess(request);
        public Task<JevAnswer> ChooseAsync(JevRequest request, CancellationToken cancellationToken) => Choose(request);
    }
    private sealed class Handler(string body) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
