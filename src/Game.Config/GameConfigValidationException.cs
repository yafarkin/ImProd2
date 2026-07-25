namespace Game.Config;

/// <summary>
/// Брошено, когда GameConfig не проходит валидацию: сломан JSON или нарушена ссылочная
/// целостность каталога (Блок 2.2). Содержит все найденные проблемы, а не только первую.
/// </summary>
public sealed class GameConfigValidationException : Exception
{
    /// <summary>Все найденные проблемы, каждая — отдельное человекочитаемое сообщение.</summary>
    public IReadOnlyList<string> Errors { get; }

    public GameConfigValidationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        var bulletList = string.Join(Environment.NewLine, errors.Select(error => "- " + error));

        return $"GameConfig is invalid ({errors.Count} problem(s)):{Environment.NewLine}{bulletList}";
    }
}
