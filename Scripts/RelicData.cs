namespace SlayCard;

// 遗物数据：最小可运行版本（纯代码）。
public sealed class RelicData
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    public RelicData(string id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }
}
