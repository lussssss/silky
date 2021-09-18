using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Silky.Core;
using Silky.Core.Exceptions;
using Silky.Core.Extensions;
using Silky.Rpc.Address.Descriptor;
using Silky.Rpc.Configuration;
using Silky.Rpc.Runtime.Server;

namespace Silky.Rpc.Address
{
    public static class AddressHelper
    {
        private const string ANYHOST = "0.0.0.0";
        private const int MIN_PORT = 0;
        private const int MAX_PORT = 65535;

        private const string LOCAL_IP_PATTERN = "127(\\.\\d{1,3}){3}$";
        private const string LOCAL_HOSTADRRESS = "localhost";
        private const string IP_PATTERN = "\\d{1,3}(\\.\\d{1,3}){3,5}$";

        public static string GetHostIp(string hostAddress)
        {
            var result = hostAddress;
            if ((!IsValidAddress(hostAddress) && !IsLocalHost(hostAddress)) || IsAnyHost(hostAddress))
            {
                result = GetAnyHostIp();
            }

            return result;
        }

        public static AddressDescriptor GetLocalWebAddressDescriptor()
        {
            var server = EngineContext.Current.Resolve<IServer>();
            Check.NotNull(server, nameof(server));
            var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
            if (address.IsNullOrEmpty())
            {
                throw new SilkyException("Failed to obtain http service rpcAddress");
            }

            var addressDescriptor = ParseAddress(address);
            return addressDescriptor;
        }

        private static AddressDescriptor ParseAddress(string address)
        {
            var addressSegments = address.Split("://");
            var scheme = addressSegments.First();
            var serviceProtocol = ServiceProtocolUtil.GetServiceProtocol(scheme);
            var domainAndPort = addressSegments.Last().Split(":");
            var domain = domainAndPort[0];
            var port = int.Parse(domainAndPort[1]);
            return new AddressDescriptor()
            {
                Address = domain,
                Port = port,
                ServiceProtocol = serviceProtocol
            };
        }

        private static string GetAnyHostIp()
        {
            string result = "";
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nics)
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    IPInterfaceProperties pix = adapter.GetIPProperties();
                    UnicastIPAddressInformationCollection ipCollection = pix.UnicastAddresses;
                    foreach (UnicastIPAddressInformation ipaddr in ipCollection)
                    {
                        if (ipaddr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            result = ipaddr.Address.ToString();
                            break;
                        }
                    }
                }
            }

            return result;
        }

        public static IRpcAddress GetRpcAddressModel()
        {
            var rpcOptions = EngineContext.Current.GetOptionsSnapshot<RpcOptions>();
            string host = GetHostIp(rpcOptions.Host);
            int port = rpcOptions.Port;
            var address = new RpcAddress(host, port, ServiceProtocol.Tcp);
            return address;
        }

        public static AddressDescriptor GetLocalAddressDescriptor()
        {
            if (EngineContext.Current.IsContainHttpCoreModule())
            {
                return GetLocalWebAddressDescriptor();
            }

            return GetRpcAddressModel().Descriptor;

        }

        public static string GetLocalAddress()
        {
            string host = GetAnyHostIp();
            return host;
        }


        public static IRpcAddress GetAddressModel(int port, ServiceProtocol serviceProtocol)
        {
            string host = GetHostIp(GetAnyHostIp());
            var address = new RpcAddress(host, port, serviceProtocol);
            return address;
        }

        public static IRpcAddress CreateAddressModel(string host, int port, ServiceProtocol serviceProtocol)
        {
            var address = new RpcAddress(host, port, serviceProtocol);
            return address;
        }
        
        public static string GetIp(string address)
        {
            if (IsValidIp(address))
            {
                return address;
            }
            var ips = Dns.GetHostAddresses(address);
            return ips[0].ToString();
        }

        public static bool IsValidIp(string address)
        {
            if (Regex.IsMatch(address, "[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}"))
            {
                string[] ips = address.Split('.');
                if (ips.Length == 4 || ips.Length == 6)
                {
                    if (int.Parse(ips[0]) < 256 && int.Parse(ips[1]) < 256 && int.Parse(ips[2]) < 256 && int.Parse(ips[3]) < 256)
                    {
                        return true;
                    }
                }
                return false;
            }
            return false;
        }

        private static bool IsValidAddress(string address)
        {
            return (address != null
                    && !ANYHOST.Equals(address)
                    && address.IsMatch(IP_PATTERN));
        }

        private static bool IsAnyHost(String host)
        {
            return ANYHOST.Equals(host);
        }
        
        private static bool IsLocalHost(string host)
        {
            return host != null
                   && (host.IsMatch(LOCAL_IP_PATTERN)
                       || host.Equals(LOCAL_HOSTADRRESS, StringComparison.OrdinalIgnoreCase));
        }

        public static IRpcAddress CreateAddressModel(string address, ServiceProtocol serviceProtocol)
        {
            var addressInfo = address.Split(":");
            return CreateAddressModel(addressInfo[0], addressInfo[1].To<int>(), serviceProtocol);
        }
    }
}