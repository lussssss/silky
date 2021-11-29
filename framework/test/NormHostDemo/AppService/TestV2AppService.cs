using System;
using System.Threading.Tasks;
using ITestApplication.Test;
using ITestApplication.Test.Dtos;
using Silky.Core.Exceptions;
using Silky.Core.Serialization;
using Silky.Rpc.Runtime.Server;

namespace NormHostDemo.AppService
{
    [ServiceKey("v2", 1)]
    public class TestV2AppService : ITestAppService
    {
        private readonly ISerializer _serializer;

        public TestV2AppService(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public async Task<TestOut> Create(TestInput input)
        {
            throw new BusinessException("无法执行v2的服务");
            return new()
            {
                Address = input.Address,
                Name = input.Name + "v2",
            };
        }

        public Task CreateOrUpdateAsync(TestInput input)
        {
            throw new NotImplementedException();
        }

        public Task<TestOut> Get(long id)
        {
            throw new System.NotImplementedException();
        }

        public Task<string> Update(TestInput input)
        {
            throw new System.NotImplementedException();
        }

        public async Task<string> DeleteAsync(TestInput input)
        {
            return _serializer.Serialize(input);
        }

        public Task<string> Search(TestInput query)
        {
            throw new System.NotImplementedException();
        }

        public string Form(TestInput query)
        {
            throw new System.NotImplementedException();
        }

        public async Task<TestOut> Get(string name)
        {
            return new()
            {
                Name = name + "v2"
            };
        }

        public Task<TestOut> GetById(long id)
        {
            throw new System.NotImplementedException();
        }


        public Task<string> UpdatePart(TestInput input)
        {
            throw new System.NotImplementedException();
        }
    }
}