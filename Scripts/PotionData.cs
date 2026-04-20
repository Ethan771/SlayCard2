namespace SlayCard;

// 药水数据：最小可运行版本（纯代码）。
public sealed class PotionData
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    public PotionData(string id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }
}
