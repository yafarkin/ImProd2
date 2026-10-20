using Game.Engine;

namespace Game.Bots.Llm.Tests;

public sealed class FileBotProtocolTests
{
    [Fact]
    public void BuildThenHire_UsesNewAlias_ThroughJournal()
    {
        var (session,id)=TestSession.StartSingleTeamSession();
        var results=FileBotProtocol.Execute("""{"requestId":"turn-1","actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine"},{"kind":"setWorkerCount","factory":"new:iron-mine","count":10}]}""",
            "turn-1",session,id,new Dictionary<string,Ulid>(),new Random(42),12);
        Assert.Equal(2,results.Count);
        Assert.All(results,r=>Assert.StartsWith("ok:",r));
        Assert.Equal(10,Assert.Single(session.State.Teams[id].Factories).DesiredWorkers);
        Assert.Contains(session.Entries,e=>e.Change is WorkerCountSet);
    }

    [Theory]
    [InlineData("other",12)]
    [InlineData("turn-1",0)]
    public void WrongRequestOrOversizeBatch_DoesNotMutate(string requestId,int cap)
    {
        var (session,id)=TestSession.StartSingleTeamSession();
        var count=session.Entries.Count;
        Assert.Throws<InvalidOperationException>(()=>FileBotProtocol.Execute("""{"requestId":"turn-1","actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}]}""",
            requestId,session,id,new Dictionary<string,Ulid>(),new Random(42),cap));
        Assert.Equal(count,session.Entries.Count);
    }

    [Fact]
    public void AliasAndDomainErrors_AreReportedWithoutDirectMutation()
    {
        var (session,id)=TestSession.StartSingleTeamSession();
        var count=session.Entries.Count;
        var results=FileBotProtocol.Execute("""{"requestId":"turn-1","actions":[{"kind":"setWorkerCount","factory":"f99","count":10},{"kind":"buildFactory","factoryDefinitionId":"missing"}]}""",
            "turn-1",session,id,new Dictionary<string,Ulid>(),new Random(42),12);
        Assert.All(results,r=>Assert.StartsWith("error:",r));
        Assert.Equal(count,session.Entries.Count);
    }
}
