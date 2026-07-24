namespace Game.Domain;

public sealed record Sector
{
    public string Id { get; }
    public string Name { get; }

    public Sector(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Sector id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sector name must not be empty.", nameof(name));

        Id = id;
        Name = name;
    }
}
