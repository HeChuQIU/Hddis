using System.Collections.Concurrent;
using Hddis.DataNode.Objects;

namespace Hddis.DataNode.Services;

public class StorageService:IStorageService
{
    private ConcurrentQueue<Command> _commandQueue = [];


    public IHddisObject Operate(Command command)
    {
        _commandQueue.Enqueue(command);
        throw new NotImplementedException();
    }
}

public interface IStorageService
{
    IHddisObject Operate(Command command);
}