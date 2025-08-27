namespace Hddis.DataNode.Objects;

public interface IHddisObject
{
    HddisObjects.Types Type { get; }

    long Expiration { get; }
}