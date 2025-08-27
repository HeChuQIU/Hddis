using System.Collections.Concurrent;
using Grpc.Core;
using LanguageExt;

namespace Hddis.DataNode.Services;

public class RedisRespServer(ILogger<RedisRespServer> logger) : RedisRespService.RedisRespServiceBase
{
    private readonly ConcurrentDictionary<string, string> _testKv = [];

    public override Task<RedisResponse> Set(KeyValueRequest request, ServerCallContext context)
    {
        var key = request.Key!;
        var value = request.Value.StringValue.Value;

        logger.LogInformation("Setting key-value pair: {Key} = {Value}", key, value);

        _testKv[key] = value;

        logger.LogInformation("Key {Key} set successfully", key);

        return Task.FromResult(new RedisResponse
        {
            Value = new RedisValue
            {
                OkValue = true
            }
        });
    }

    public override Task<RedisResponse> Get(KeyRequest request, ServerCallContext context)
    {
        var key = request.Key;

        logger.LogInformation("Getting value for key: {Key}", key);

        var hasKey = _testKv.TryGetValue(key, out var value);

        if (hasKey)
        {
            logger.LogInformation("Key {Key} found with value: {Value}", key, value);
        }
        else
        {
            logger.LogWarning("Key {Key} not found", key);
        }

        var redisResponse = hasKey.Apply(b => b
                ? new RedisValue
                {
                    StringValue = new StringValue { Value = value! }
                }
                : new RedisValue
                {
                    NullValue = true
                })
            .Apply(redisValue => new RedisResponse
            {
                Value = redisValue
            });

        return Task.FromResult(redisResponse);
    }
}