using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Castle.Core.Internal;
using Microsoft.Extensions.Configuration;
using Silky.Core;
using Silky.Core.Exceptions;
using Silky.Core.Extensions.Collections.Generic;
using Silky.Core.Rpc;
using Silky.Http.Dashboard.AppService.Dtos;
using Silky.Http.Dashboard.Configuration;
using Silky.Rpc.AppServices.Dtos;
using Silky.Rpc.CachingInterceptor.Providers;
using Silky.Rpc.Endpoint;
using Silky.Rpc.Endpoint.Descriptor;
using Silky.Rpc.RegistryCenters;
using Silky.Rpc.Runtime.Client;
using Silky.Rpc.Runtime.Server;
using Silky.Rpc.Utils;

namespace Silky.Http.Dashboard.AppService
{
    public class SilkyAppService : ISilkyAppService
    {
        private readonly IServerManager _serverManager;
        private readonly IServiceEntryManager _serviceEntryManager;
        private readonly IServiceEntryLocator _serviceEntryLocator;
        private readonly IRemoteExecutor _remoteExecutor;
        private readonly ILocalExecutor _localExecutor;
        private readonly IRegisterCenterHealthProvider _registerCenterHealthProvider;


        private const string ipEndpointRegex =
            @"([0-9]|[1-9]\d{1,3}|[1-5]\d{4}|6[0-4]\d{3}|65[0-4]\d{2}|655[0-2]\d|6553[0-5])";

        private const string getInstanceSupervisorServiceEntryId =
            "Silky.Rpc.AppServices.IRpcAppService.GetInstanceDetail_Get";

        private const string getGetServiceEntrySupervisorServiceHandle =
            "Silky.Rpc.AppServices.IRpcAppService.GetServiceEntryHandleInfos_Get";

        private const string getGetServiceEntrySupervisorServiceInvoke =
            "Silky.Rpc.AppServices.IRpcAppService.GetServiceEntryInvokeInfos_Get";

        public SilkyAppService(
            IServerManager serverManager,
            IServiceEntryManager serviceEntryManager,
            IServiceEntryLocator serviceEntryLocator,
            IRemoteExecutor remoteExecutor,
            ILocalExecutor localExecutor,
            IRegisterCenterHealthProvider registerCenterHealthProvider)
        {
            _serverManager = serverManager;
            _serviceEntryManager = serviceEntryManager;

            _remoteExecutor = remoteExecutor;
            _registerCenterHealthProvider = registerCenterHealthProvider;
            _localExecutor = localExecutor;
            _serviceEntryLocator = serviceEntryLocator;
        }

        public PagedList<GetHostOutput> GetHosts(PagedRequestDto input)
        {
            return GetAllHosts().ToPagedList(input.PageIndex, input.PageSize);
        }

        public IReadOnlyCollection<GetHostOutput> GetAllHosts()
        {
            var serverDescriptors = _serverManager
                .ServerDescriptors
                .Select(p => new GetHostOutput()
                {
                    AppServiceCount = p.Services.Length,
                    InstanceCount = p.Endpoints.Count(p => p.IsInstanceEndpoint()),
                    HostName = p.HostName,
                    ServiceProtocols = p.Endpoints.Select(p => p.ServiceProtocol).Distinct().ToArray(),
                    ServiceEntriesCount = p.Services.SelectMany(p => p.ServiceEntries).Count()
                });
            return serverDescriptors.ToArray();
        }

        public ServerDescriptor GetHostDetail(string hostName)
        {
            var serverDescriptor = _serverManager.GetServerDescriptor(hostName);
            if (serverDescriptor == null)
            {
                throw new BusinessException($"There is no server for {hostName}");
            }

            return serverDescriptor;
        }

        public PagedList<GetHostInstanceOutput> GetHostInstances(string hostName,
            GetHostInstanceInput input)
        {
            var server = _serverManager.GetServer(hostName);
            if (server == null)
            {
                throw new BusinessException($"There is no server for {hostName}");
            }

            var hostInstances = server.Endpoints
                    .Where(p => p.Descriptor.IsInstanceEndpoint())
                    .Select(p => new GetHostInstanceOutput()
                    {
                        HostName = server.HostName,
                        IsHealth = SocketCheck.TestConnection(p.Host, p.Port),
                        Address = p.GetAddress(),
                        Host = p.Host,
                        Port = p.Port,
                        IsEnable = p.Enabled,
                        LastDisableTime = p.LastDisableTime,
                        ServiceProtocol = p.ServiceProtocol,
                    })
                    .WhereIf(input.ServiceProtocol.HasValue, p => p.ServiceProtocol == input.ServiceProtocol)
                ;

            return hostInstances.ToPagedList(input.PageIndex, input.PageSize);
        }

        public PagedList<GetWebSocketServiceOutput> GetWebSocketServices(string hostName, PagedRequestDto input)
        {
            var webSocketServiceOutputs = new List<GetWebSocketServiceOutput>();

            var server = _serverManager.GetServer(hostName);
            var wsServices = server.Services.Where(p => p.ServiceProtocol == ServiceProtocol.Ws);
            var endpoints = server.Endpoints.Where(p => p.ServiceProtocol == ServiceProtocol.Ws);
            foreach (var wsService in wsServices)
            {
                foreach (var endpoint in endpoints)
                {
                    var webSocketServiceOutput = new GetWebSocketServiceOutput()
                    {
                        HostName = server.HostName,
                        ServiceId = wsService.Id,
                        ServiceName = wsService.ServiceName,
                        Address = endpoint.GetAddress(),
                        Path = wsService.GetWsPath()
                    };
                    webSocketServiceOutputs.Add(webSocketServiceOutput);
                }
            }
            
            return webSocketServiceOutputs.ToPagedList(input.PageIndex,input.PageSize);
        }

        public IReadOnlyCollection<GetServiceOutput> GetServices(string hostName)
        {
            return _serverManager.ServerDescriptors.SelectMany(p => p.Services.Select(s => new GetServiceOutput()
                {
                    HostName = p.HostName,
                    ServiceName = s.ServiceName,
                    ServiceId = s.Id,
                    ServiceProtocol = s.ServiceProtocol
                }))
                .Distinct()
                .WhereIf(!hostName.IsNullOrEmpty(), p => p.HostName.Equals(hostName))
                .ToArray();
        }

        public GetGatewayOutput GetGateway()
        {
            var gateway = _serverManager.GetServer(EngineContext.Current.HostName);
            return new GetGatewayOutput()
            {
                HostName = gateway.HostName,
                InstanceCount = gateway.Endpoints.Count(p => p.ServiceProtocol.IsHttp())
            };
        }

        public PagedList<GetGatewayInstanceOutput> GetGatewayInstances(PagedRequestDto input)
        {
            var gateway = _serverManager.GetServer(EngineContext.Current.HostName);

            var gatewayInstances = new List<GetGatewayInstanceOutput>();
            var gatewayEndpoints = gateway.Endpoints.Where(p => p.ServiceProtocol.IsHttp());
            foreach (var addressDescriptor in gatewayEndpoints)
            {
                var gatewayInstance = new GetGatewayInstanceOutput()
                {
                    HostName = gateway.HostName,
                    Address = addressDescriptor.Host,
                    Port = addressDescriptor.Port,
                };
                gatewayInstances.Add(gatewayInstance);
            }

            return gatewayInstances.ToPagedList(input.PageIndex, input.PageSize);
        }

        public PagedList<GetServiceEntryOutput> GetServiceEntries(GetServiceEntryInput input)
        {
            var serviceEntryOutputs = GetAllServiceEntryFromCache();

            serviceEntryOutputs = serviceEntryOutputs
                .WhereIf(!input.HostName.IsNullOrEmpty(), p => input.HostName.Equals(p.HostName))
                .WhereIf(!input.ServiceId.IsNullOrEmpty(), p => input.ServiceId.Equals(p.ServiceId))
                .WhereIf(!input.ServiceEntryId.IsNullOrEmpty(), p => input.ServiceEntryId.Equals(p.ServiceEntryId))
                .WhereIf(!input.Name.IsNullOrEmpty(), p => p.ServiceEntryId.Contains(input.Name))
                .WhereIf(input.IsEnable.HasValue, p => p.IsEnable == input.IsEnable)
                .OrderBy(p => p.HostName)
                .ThenBy(p => p.ServiceId)
                .ToList();
            return serviceEntryOutputs.ToPagedList(input.PageIndex, input.PageSize);
        }

        private List<GetServiceEntryOutput> GetAllServiceEntryFromCache()
        {
            var serviceEntryOutputs = new List<GetServiceEntryOutput>();
            var servers = _serverManager.Servers;
            foreach (var server in servers)
            {
                foreach (var service in server.Services)
                {
                    var serviceEntries = service.ServiceEntries.Select(p =>
                        new GetServiceEntryOutput()
                        {
                            ServiceName = service.ServiceName,
                            ServiceId = service.Id,
                            ServiceEntryId = p.Id,
                            HostName = server.HostName,
                            IsEnable = server.Endpoints.Any(p => p.Enabled),
                            ServerInstanceCount = server.Endpoints.Count(p =>
                                p.ServiceProtocol == service.ServiceProtocol && p.Enabled),
                            ServiceProtocol = p.ServiceProtocol,
                            MultipleServiceKey = service.MultiServiceKeys(),
                            Author = p.GetAuthor(),
                            ProhibitExtranet = p.ProhibitExtranet,
                            IsAllowAnonymous = p.IsAllowAnonymous,
                            WebApi = p.WebApi,
                            HttpMethod = p.HttpMethod,
                            Method = p.Method,
                            IsDistributeTransaction = p.IsDistributeTransaction,
                            ServiceKeys = service.GetServiceKeys()?.Select(p => new ServiceKeyOutput()
                            {
                                Name = p.Key,
                                Weight = p.Value
                            }).ToArray(),
                        });
                    serviceEntryOutputs.AddRange(serviceEntries);
                }
            }

            return serviceEntryOutputs;
        }

        public GetServiceEntryDetailOutput GetServiceEntryDetail(string serviceEntryId)
        {
            var serviceEntryOutput =
                GetAllServiceEntryFromCache().FirstOrDefault(p => p.ServiceEntryId == serviceEntryId);
            if (serviceEntryOutput == null)
            {
                throw new BusinessException($"There is no service entry with id {serviceEntryId}");
            }

            var serviceEntry = _serviceEntryLocator.GetServiceEntryById(serviceEntryOutput.ServiceEntryId);
            var serviceEntryDetailOutput = new GetServiceEntryDetailOutput()
            {
                HostName = serviceEntryOutput.HostName,
                ServiceEntryId = serviceEntryOutput.ServiceEntryId,
                ServiceId = serviceEntryOutput.ServiceId,
                ServiceName = serviceEntryOutput.ServiceName,
                ServiceProtocol = serviceEntryOutput.ServiceProtocol,
                Author = serviceEntryOutput.Author,
                WebApi = serviceEntryOutput.WebApi,
                HttpMethod = serviceEntryOutput.HttpMethod,
                ProhibitExtranet = serviceEntryOutput.ProhibitExtranet,
                Method = serviceEntryOutput.Method,
                MultipleServiceKey = serviceEntryOutput.MultipleServiceKey,
                IsEnable = serviceEntryOutput.IsEnable,
                ServerInstanceCount = serviceEntryOutput.ServerInstanceCount,
                Governance = serviceEntry?.GovernanceOptions,
                CacheTemplates = serviceEntry?.CustomAttributes.OfType<ICachingInterceptProvider>().Select(p =>
                    new ServiceEntryCacheTemplateOutput()
                    {
                        KeyTemplete = p.KeyTemplete,
                        OnlyCurrentUserData = p.OnlyCurrentUserData,
                        CachingMethod = p.CachingMethod
                    }).ToArray(),
                ServiceKeys = serviceEntryOutput.ServiceKeys,
                IsDistributeTransaction = serviceEntryOutput.IsDistributeTransaction,
                Fallbacks = serviceEntry?.CustomAttributes.OfType<FallbackAttribute>().Select(p => new FallbackOutput()
                {
                    TypeName = p.Type.FullName,
                    MethodName = p.MethodName ?? serviceEntry?.MethodInfo.Name,
                    Weight = p.Weight
                }).ToArray()
            };

            return serviceEntryDetailOutput;
        }

        public PagedList<GetServiceEntryInstanceOutput> GetServiceEntryInstances(string serviceEntryId,
            int pageIndex = 1,
            int pageSize = 10)
        {
            var serviceEntryDescriptor = _serverManager.ServerDescriptors
                .SelectMany(p => p.Services.SelectMany(p => p.ServiceEntries))
                .FirstOrDefault(p => p.Id == serviceEntryId);
            if (serviceEntryDescriptor == null)
            {
                throw new BusinessException($"There is no service entry with id {serviceEntryId}");
            }

            var serverInstances = _serverManager.Servers.Where(p => p
                    .Services.Any(q => q.ServiceEntries.Any(e => e.Id == serviceEntryId)))
                .SelectMany(p => p.Endpoints).Where(p => p.ServiceProtocol == serviceEntryDescriptor.ServiceProtocol);

            var serviceEntryInstances = serverInstances.Select(p => new GetServiceEntryInstanceOutput()
            {
                ServiceEntryId = serviceEntryId,
                Address = p.Descriptor.GetHostAddress(),
                Enabled = p.Enabled,
                IsHealth = SocketCheck.TestConnection(p.Host, p.Port),
                ServiceProtocol = p.ServiceProtocol
            });

            return serviceEntryInstances.ToPagedList(pageIndex, pageSize);
        }

        public async Task<GetInstanceDetailOutput> GetInstanceDetail(string address)
        {
            if (!Regex.IsMatch(address, ipEndpointRegex))
            {
                throw new BusinessException($"{address} incorrect rpc address format");
            }

            var addressInfo = address.Split(":");
            if (!SocketCheck.TestConnection(addressInfo[0], int.Parse(addressInfo[1])))
            {
                throw new BusinessException($"{address} is unHealth");
            }

            var serviceEntry = _serviceEntryLocator.GetServiceEntryById(getInstanceSupervisorServiceEntryId);
            if (serviceEntry == null)
            {
                throw new BusinessException($"Not find serviceEntry by {getInstanceSupervisorServiceEntryId}");
            }

            var result = await ServiceEntryExec<GetInstanceDetailOutput>(address, serviceEntry);
            if (result?.Address != address)
            {
                throw new SilkyException("The rpc address of the routing instance is wrong");
            }

            return result;
        }

        public async Task<PagedList<ServiceEntryHandleInfo>> GetServiceEntryHandleInfos(string address,
            PagedRequestDto input)
        {
            if (!Regex.IsMatch(address, ipEndpointRegex))
            {
                throw new BusinessException($"{address} incorrect rpcAddress format");
            }

            var addressInfo = address.Split(":");
            if (!SocketCheck.TestConnection(addressInfo[0], int.Parse(addressInfo[1])))
            {
                throw new BusinessException($"{address} is unHealth");
            }


            var serviceEntry = _serviceEntryLocator.GetServiceEntryById(getGetServiceEntrySupervisorServiceHandle);
            if (serviceEntry == null)
            {
                throw new BusinessException($"Not find serviceEntry by {getGetServiceEntrySupervisorServiceHandle}");
            }

            var result = await ServiceEntryExec<IReadOnlyCollection<ServiceEntryHandleInfo>>(address, serviceEntry);

            return result.ToPagedList(input.PageIndex, input.PageSize);
        }

        private bool IsLocalAddress(string address)
        {
            var localAddress = RpcEndpointHelper.GetLocalRpcEndpointDescriptor().GetHostAddress();
            return localAddress.Equals(address);
        }


        public async Task<PagedList<ServiceEntryInvokeInfo>> GetServiceEntryInvokeInfos(string address,
            PagedRequestDto input)
        {
            if (!Regex.IsMatch(address, ipEndpointRegex))
            {
                throw new BusinessException($"{address} incorrect rpc address format");
            }

            var addressInfo = address.Split(":");
            if (!SocketCheck.TestConnection(addressInfo[0], int.Parse(addressInfo[1])))
            {
                throw new BusinessException($"{address} is unHealth");
            }

            var serviceEntry = _serviceEntryLocator.GetServiceEntryById(getGetServiceEntrySupervisorServiceInvoke);
            if (serviceEntry == null)
            {
                throw new BusinessException($"Not find serviceEntry by {getGetServiceEntrySupervisorServiceInvoke}");
            }

            var result = await ServiceEntryExec<IReadOnlyCollection<ServiceEntryInvokeInfo>>(address, serviceEntry);

            return result.ToPagedList(input.PageIndex, input.PageSize);
        }


        public IReadOnlyCollection<GetRegistryCenterOutput> GetRegistryCenters()
        {
            var registerCenterInfos = _registerCenterHealthProvider.GetRegistryCenterHealthInfo();
            return registerCenterInfos.Select(p =>
                new GetRegistryCenterOutput()
                {
                    RegistryCenterAddress = p.Key,
                    IsHealth = p.Value.IsHealth,
                    UnHealthReason = p.Value.UnHealthReason,
                    UnHealthTimes = p.Value.UnHealthTimes,
                    RegistryCenterType = EngineContext.Current.Configuration.GetValue<string>("registrycenter:type")
                }).ToArray();
        }

        public IReadOnlyCollection<GetProfileOutput> GetProfiles()
        {
            var getProfileOutputs = new List<GetProfileOutput>();
            getProfileOutputs.Add(new GetProfileOutput()
            {
                Code = "Microservice",
                Title = "微服务应用",
                Count = _serverManager.ServerDescriptors.Count
            });
            getProfileOutputs.Add(new GetProfileOutput()
            {
                Code = "ServiceInstance",
                Title = "微服务应用实例",
                Count = _serverManager.ServerDescriptors.SelectMany(p => p.Endpoints).Count(p => p.IsInstanceEndpoint())
            });

            getProfileOutputs.Add(new GetProfileOutput()
            {
                Code = "WebSocketServiceInstance",
                Title = "支持WebSocket的应用实例",
                Count = _serverManager.ServerDescriptors.SelectMany(p => p.Endpoints)
                    .Count(p => p.ServiceProtocol == ServiceProtocol.Ws)
            });
            getProfileOutputs.Add(new GetProfileOutput()
            {
                Code = "Services",
                Title = "应用服务",
                Count = _serverManager.ServerDescriptors.SelectMany(p => p.Services)
                    .Distinct().Count()
            });
            getProfileOutputs.Add(new GetProfileOutput()
            {
                Code = "WebSocketService",
                Title = "WebSocket服务",
                Count = _serverManager.ServerDescriptors.SelectMany(p => p.Services)
                    .Where(p => p.ServiceProtocol == ServiceProtocol.Ws)
                    .Distinct().Count()
            });
            getProfileOutputs.Add(new GetProfileOutput()
            {
                Code = "ServiceEntry",
                Title = "服务条目",
                Count = _serverManager.ServerDescriptors.SelectMany(p => p.Services.SelectMany(p => p.ServiceEntries))
                    .Distinct().Count()
            });
            getProfileOutputs.Add(new GetProfileOutput()
            {
                Code = "RegistryCenter",
                Title = "服务注册中心",
                Count = _registerCenterHealthProvider.GetRegistryCenterHealthInfo().Count
            });

            return getProfileOutputs.Where(p => p.Count > 0).ToArray();
        }

        public IReadOnlyCollection<GetExternalRouteOutput> GetExternalRoutes()
        {
            var externalRoutes = new List<GetExternalRouteOutput>();
            var dashboardOptions = EngineContext.Current.GetOptionsSnapshot<DashboardOptions>();
            if (dashboardOptions.ExternalLinks != null && dashboardOptions.ExternalLinks.Any())
            {
                var externalRoute = CreateExternalRoute("/external");
                externalRoute.Meta["Icon"] = "el-icon-link";
                externalRoute.Meta["Title"] = "外部链接";
                externalRoute.Meta["IsLayout"] = true;
                externalRoute.Meta["ShowLink"] = true;
                externalRoute.Meta["SavedPosition"] = false;
                externalRoute.Name = "external";
                foreach (var externalLink in dashboardOptions.ExternalLinks)
                {
                    var externalRouteChild = CreateExternalRoute(externalLink.Path);
                    externalRouteChild.Meta["Icon"] = externalLink.Icon ?? "el-icon-link";
                    externalRouteChild.Meta["Title"] = externalLink.Title;
                    externalRouteChild.Meta["ShowLink"] = true;
                    externalRouteChild.Meta["ExternalLink"] = true;
                    externalRoute.Meta["SavedPosition"] = false;
                    if (externalRoute.Children.All(p => p.Path != externalRouteChild.Path))
                    {
                        externalRoute.Children.Add(externalRouteChild);
                    }
                }

                externalRoutes.Add(externalRoute);
            }

            return externalRoutes.ToArray();
        }


        private GetExternalRouteOutput CreateExternalRoute(string path)
        {
            var externalRoute = new GetExternalRouteOutput()
            {
                Path = path,
                Meta = new Dictionary<string, object>()
            };
            return externalRoute;
        }

        private async Task<T> ServiceEntryExec<T>(string address, ServiceEntry serviceEntry)
        {
            T result = default(T);
            if (IsLocalAddress(address))
            {
                result =
                    (T)await _localExecutor.Execute(serviceEntry, Array.Empty<object>(), null);
            }
            else
            {
                RpcContext.Context.SetAttachment(AttachmentKeys.SelectedServerEndpoint, address);
                result =
                    (T)await _remoteExecutor.Execute(serviceEntry, Array.Empty<object>(), null);
            }

            return result;
        }
    }
}