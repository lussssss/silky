using AspNetCoreRateLimit;
using GatewayDemo.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Silky.Http.MiniProfiler;

namespace GatewayDemo
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IAuthorizationHandler, TestAuthorizationHandler>();
            services.AddSwaggerDocuments();
            services.AddSilkyMiniProfiler();
           // services.AddDashboard();
            services.AddSilkyIdentity();
            services.AddSilkySkyApm();
            services.AddMessagePackCodec();
            var redisOptions = Configuration.GetRateLimitRedisOptions();
            services.AddClientRateLimit(redisOptions);
            services.AddIpRateLimit(redisOptions);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwaggerDocuments();
                app.UseMiniProfiler();
            }

            app.UseClientRateLimiting();
            app.UseIpRateLimiting();
            app.UseSilkyIdentity();
          //  app.UseDashboard();
            app.ConfigureSilkyRequestPipeline();
            // app.UseEndpoints(endpoints=> endpoints.MapControllers());
        }
    }
}