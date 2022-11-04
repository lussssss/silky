using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Silky.Rpc.Routing;
using Silky.Rpc.Security;

namespace ITestApplication.Mq;

[ServiceRoute]
[Authorize]
public interface IMqAppService
{
    [HttpPost]
    Task Publish(string data);

    [HttpPost]
    Task Send(string data);
}