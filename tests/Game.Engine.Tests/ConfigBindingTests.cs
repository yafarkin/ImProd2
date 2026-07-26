namespace Game.Engine.Tests;

/// <summary>
/// Привязка журнала к конфигу: первая запись (<see cref="SessionStarted"/>) несёт контент-хеш
/// GameConfig, и при доигрывании поверх другого/подменённого конфига это обнаруживается (Правка
/// overview-ревизии перед Фазой 6).
/// </summary>
public class ConfigBindingTests
{
    [Fact]
    public void SessionStarted_With_A_Hash_That_Does_Not_Match_The_Supplied_Config_Throws()
    {
        // Доигрывание журнала поверх другого конфига: записанный в событии хеш не совпадает с
        // хешем фактически поданного конфига (state.Config) — восстановление обязано это заметить.
        var state = new GameSessionState(TestGameConfig.Resolved);
        var started = new SessionStarted
        {
            Id = Ulid.NewUlid(),
            PresetId = "test",
            EndTurn = 999,
            ConfigHash = new string('0', 64), // заведомо чужой хеш
            Teams = Array.Empty<TeamSpec>(),
        };

        Assert.Throws<InvalidOperationException>(() => started.Apply(state));
    }

    [Fact]
    public void SessionStarted_Records_The_Config_Hash_When_It_Matches()
    {
        var state = new GameSessionState(TestGameConfig.Resolved);
        var started = new SessionStarted
        {
            Id = Ulid.NewUlid(),
            PresetId = "test",
            EndTurn = 999,
            ConfigHash = TestGameConfig.Resolved.ContentHash,
            Teams = Array.Empty<TeamSpec>(),
        };

        started.Apply(state);

        Assert.Equal(TestGameConfig.Resolved.ContentHash, state.ConfigHash);
    }
}
