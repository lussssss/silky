using System.Linq;
using System.Threading.Tasks;
using ITestApplication.File;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TestApplication.File;

public class FileAppService : IFileAppService
{
    public Task PostFile(IFormFile file)
    {
        throw new System.NotImplementedException();
    }

    public Task PostFiles(IFormFileCollection files)
    {
        throw new System.NotImplementedException();
    }

    public Task PostFormWithFile(FormWithFile formWithFile)
    {
        throw new System.NotImplementedException();
    }

    public async Task<IActionResult> Download()
    {
        var bytes = System.IO.File.ReadAllBytes("./Contents/测试.xlsx").ToArray();
        return new FileContentResult(bytes, "application/octet-stream")
        {
            FileDownloadName = "测试文件.xlsx",
        };
    }
}