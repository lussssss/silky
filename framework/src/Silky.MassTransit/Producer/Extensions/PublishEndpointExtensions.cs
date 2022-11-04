using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Silky.Core.Runtime.Rpc;
using Silky.Core.Runtime.Session;
using Silky.MassTransit.Extensions;

namespace MassTransit;

public static class PublishEndpointExtensions
{
    public static Task PublishForSilky<T>(
        this IPublishEndpoint endpoint,
        T message,
        CancellationToken cancellationToken = default(CancellationToken))
        where T : class
    {
        RpcContext.Context.SetMqInvokeAddressInfo();
        return endpoint.Publish(message, context =>
        {
            foreach (var header in RpcContext.Context.GetInvokeAttachments())
            {
                context.Headers.Set(header.Key, header.Value);
            }
        }, cancellationToken);
    }

    public static Task PublishForSilky(
        this IPublishEndpoint endpoint,
        object message,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        RpcContext.Context.SetMqInvokeAddressInfo();
        return endpoint.Publish(message, context =>
        {
            foreach (var header in  RpcContext.Context.GetInvokeAttachments())
            {
                context.Headers.Set(header.Key, header.Value);
            }
        }, cancellationToken);
    }
    
    public static Task PublishBatchForSilky<T>(
        this IPublishEndpoint endpoint,
        IEnumerable<T> messages,
        CancellationToken cancellationToken = default(CancellationToken))
        where T : class
    {
        RpcContext.Context.SetMqInvokeAddressInfo();
        return endpoint.PublishBatch(messages, context =>
        {
            foreach (var header in RpcContext.Context.GetInvokeAttachments())
            {
                context.Headers.Set(header.Key, header.Value);
            }
        }, cancellationToken);
    }
    
    public static Task PublishBatchForSilky(
        this IPublishEndpoint endpoint,
        IEnumerable<object> messages,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        RpcContext.Context.SetMqInvokeAddressInfo();
        return endpoint.PublishBatch(messages, context =>
        {
            foreach (var header in RpcContext.Context.GetInvokeAttachments())
            {
                context.Headers.Set(header.Key, header.Value);
            }
        }, cancellationToken);
    }
    
}