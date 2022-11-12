﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silky.Core.DependencyInjection;
using Silky.Core.DynamicProxy;
using Silky.Core.Extensions;
using Silky.Core.Logging;
using Silky.Core.MiniProfiler;
using Silky.Rpc.Extensions;
using Silky.Rpc.Runtime.Server;

namespace Silky.Rpc.CachingInterceptor
{
    public class CachingInterceptor : SilkyInterceptor, ITransientDependency
    {
        private readonly IDistributedInterceptCache _distributedCache;
        private ILogger<CachingInterceptor> Logger { get; set; }

        public CachingInterceptor(IDistributedInterceptCache distributedCache)
        {
            _distributedCache = distributedCache;
            Logger = NullLogger<CachingInterceptor>.Instance;
        }

        public override async Task InterceptAsync(ISilkyMethodInvocation invocation)
        {
            var serviceEntry = invocation.GetServiceEntry();
            var serviceEntryDescriptor = invocation.GetServiceEntryDescriptor();
            if (serviceEntry?.GovernanceOptions.EnableCachingInterceptor == true &&
                serviceEntry?.CachingInterceptorDescriptors?.Any() == true)
            {
                await InterceptForServiceEntryAsync(invocation, serviceEntry);
            }
            else if (serviceEntryDescriptor?.GovernanceOptions.EnableCachingInterceptor == true &&
                     serviceEntryDescriptor?.CachingInterceptorDescriptors?.Any() == true)
            {
                await InterceptForServiceEntryDescriptorAsync(invocation, serviceEntryDescriptor);
            }
            else
            {
                await invocation.ProceedAsync();
            }
        }

        private async Task InterceptForServiceEntryDescriptorAsync(ISilkyMethodInvocation invocation,
            ServiceEntryDescriptor serviceEntryDescriptor)
        {
            var serviceKey = invocation.GetServiceKey();
            var parameters = invocation.GetServiceEntryDescriptorParameters();
            var proceed = ProceedType.UnProceed;

            async Task InvocationProceedAsync(ISilkyMethodInvocation invocation)
            {
                if (proceed == ProceedType.UnProceed)
                {
                    await invocation.ProceedAsync();
                    proceed = ProceedType.ForCache;
                }
            }

            async Task<object> GetResultFirstFromCache(string cacheName, string cacheKey)
            {
                _distributedCache.UpdateCacheName(cacheName);
                var result = await _distributedCache.GetOrAddAsync(cacheKey,
                    async () =>
                    {
                        await InvocationProceedAsync(invocation);
                        return invocation.ReturnValue;
                    });
                if (proceed == ProceedType.UnProceed)
                {
                    proceed = ProceedType.ForCache;
                }

                return result;
            }

            var cachingInterceptorDescriptors = serviceEntryDescriptor.CachingInterceptorDescriptors;
            var removeCachingInterceptorDescriptors =
                cachingInterceptorDescriptors.Where(p => p.CachingMethod == CachingMethod.Remove);
            var getCachingInterceptProviderDescriptor =
                cachingInterceptorDescriptors.FirstOrDefault(p => p.CachingMethod == CachingMethod.Get);
            var updateCachingInterceptProviderDescriptors =
                cachingInterceptorDescriptors.Where(p => p.CachingMethod == CachingMethod.Update);

            if (getCachingInterceptProviderDescriptor != null)
            {
                if (serviceEntryDescriptor.IsDistributeTransaction)
                {
                    await InvocationProceedAsync(invocation);
                }
                else
                {
                    _distributedCache.SetIgnoreMultiTenancy(
                        getCachingInterceptProviderDescriptor.IgnoreMultiTenancy);

                    var getCacheKey = CacheKeyHelper.GetCachingInterceptKey(parameters,
                        getCachingInterceptProviderDescriptor, serviceKey);

                    invocation.ReturnValue = await GetResultFirstFromCache(
                        getCachingInterceptProviderDescriptor.CacheName,
                        getCacheKey);
                }
            }

            if (updateCachingInterceptProviderDescriptors.Any())
            {
                await InvocationProceedAsync(invocation);
                foreach (var updateCachingInterceptProviderDescriptor in
                         updateCachingInterceptProviderDescriptors)
                {
                    _distributedCache.SetIgnoreMultiTenancy(updateCachingInterceptProviderDescriptor
                        .IgnoreMultiTenancy);
                    var updateCacheKey = CacheKeyHelper.GetCachingInterceptKey(parameters,
                        updateCachingInterceptProviderDescriptor, serviceKey);
                    _distributedCache.UpdateCacheName(updateCachingInterceptProviderDescriptor.CacheName);
                    await _distributedCache.SetAsync(updateCacheKey, invocation.ReturnValue);
                }
            }

            await InvocationProceedAsync(invocation);

            if (removeCachingInterceptorDescriptors.Any() && proceed == ProceedType.ForExec)
            {
                foreach (var removeCachingInterceptProvider in removeCachingInterceptorDescriptors)
                {
                    _distributedCache.SetIgnoreMultiTenancy(removeCachingInterceptProvider.IgnoreMultiTenancy);
                    var removeCacheKey =
                        CacheKeyHelper.GetCachingInterceptKey(parameters, removeCachingInterceptProvider, serviceKey);
                    if (removeCachingInterceptProvider.IsRemoveMatchKeyProvider)
                    {
                        await _distributedCache.RemoveMatchKeyAsync(removeCacheKey);
                    }
                    else
                    {
                        await _distributedCache.RemoveAsync(removeCacheKey);
                    }
                }
            }
        }

        private async Task InterceptForServiceEntryAsync(ISilkyMethodInvocation invocation, ServiceEntry serviceEntry)
        {
            var serviceKey = invocation.GetServiceKey();
            var parameters = invocation.GetParameters();
            var proceed = ProceedType.UnProceed;

            async Task InvocationProceedAsync(ISilkyMethodInvocation invocation)
            {
                if (proceed == ProceedType.UnProceed)
                {
                    await invocation.ProceedAsync();
                    proceed = ProceedType.ForExec;
                }
            }

            async Task<object> GetResultFirstFromCache(string cacheName, string cacheKey, ServiceEntry entry)
            {
                _distributedCache.UpdateCacheName(cacheName);
                var result = await _distributedCache.GetOrAddAsync(cacheKey,
                    serviceEntry.MethodInfo.GetReturnType(),
                    async () =>
                    {
                        await InvocationProceedAsync(invocation);
                        return invocation.ReturnValue;
                    });
                if (proceed == ProceedType.UnProceed)
                {
                    proceed = ProceedType.ForCache;
                }

                return result;
            }

            var removeCachingInterceptProviders = serviceEntry.GetAllRemoveCachingInterceptProviders();
            var getCachingInterceptProvider = serviceEntry.GetGetCachingInterceptProvider();
            var updateCachingInterceptProviders = serviceEntry.GetUpdateCachingInterceptProviders();

            if (getCachingInterceptProvider != null)
            {
                if (serviceEntry.IsTransactionServiceEntry())
                {
                    Logger.LogWithMiniProfiler(MiniProfileConstant.Caching.Name,
                        MiniProfileConstant.Caching.State.GetCaching,
                        $"Cache interception is invalid in distributed transaction processing");

                    await invocation.ProceedAsync();
                    proceed = ProceedType.ForExec;
                }
                else
                {
                    _distributedCache.SetIgnoreMultiTenancy(getCachingInterceptProvider.IgnoreMultiTenancy);
                    var getCacheKey = CacheKeyHelper.GetCachingInterceptKey(serviceEntry, parameters,
                        serviceEntry.GetGetCachingInterceptProvider(), serviceKey);
                    Logger.LogWithMiniProfiler(MiniProfileConstant.Caching.Name,
                        MiniProfileConstant.Caching.State.GetCaching,
                        $"Ready to get data from the cache service:[cacheName=>{serviceEntry.GetCacheName()};cacheKey=>{getCacheKey}]");
                    invocation.ReturnValue = await GetResultFirstFromCache(
                        serviceEntry.GetCacheName(),
                        getCacheKey,
                        serviceEntry);
                }
            }

            if (updateCachingInterceptProviders.Any())
            {
                await InvocationProceedAsync(invocation);
                var index = 1;
                foreach (var updateCachingInterceptProvider in updateCachingInterceptProviders)
                {
                    var updateCacheKey =
                        CacheKeyHelper.GetCachingInterceptKey(serviceEntry, parameters,
                            updateCachingInterceptProvider,
                            serviceKey);
                    Logger.LogWithMiniProfiler(MiniProfileConstant.Caching.Name,
                        MiniProfileConstant.Caching.State.UpdateCaching + index,
                        $"The cacheKey for updating the cache data is[cacheName=>{serviceEntry.GetCacheName()};cacheKey=>{updateCacheKey}]");
                    _distributedCache.SetIgnoreMultiTenancy(updateCachingInterceptProvider.IgnoreMultiTenancy);

                    await _distributedCache.SetAsync(updateCacheKey, invocation.ReturnValue);
                    index++;
                }
            }

            await InvocationProceedAsync(invocation);

            if (removeCachingInterceptProviders.Any() && proceed == ProceedType.ForExec)
            {
                var index = 1;

                foreach (var removeCachingInterceptProvider in serviceEntry.GetAllRemoveCachingInterceptProviders())
                {
                    _distributedCache.SetIgnoreMultiTenancy(removeCachingInterceptProvider.IgnoreMultiTenancy);
                    var removeCacheKey =
                        CacheKeyHelper.GetCachingInterceptKey(serviceEntry, parameters, removeCachingInterceptProvider,
                            serviceKey);
                    if (removeCachingInterceptProvider is IRemoveCachingInterceptProvider
                        removeCachingInterceptProvider1)
                    {
                        await _distributedCache.RemoveAsync(removeCacheKey,
                            removeCachingInterceptProvider1.CacheName,
                            true);
                    }

                    if (removeCachingInterceptProvider is IRemoveMatchKeyCachingInterceptProvider)
                    {
                        await _distributedCache.RemoveMatchKeyAsync(removeCacheKey);
                    }

                    Logger.LogWithMiniProfiler(MiniProfileConstant.Caching.Name,
                        MiniProfileConstant.Caching.State.RemoveCaching + index,
                        $"Remove the cache with key {removeCacheKey}");
                    index++;
                }
            }
        }

        private enum ProceedType
        {
            UnProceed,
            ForCache,
            ForExec,
        }
    }
}