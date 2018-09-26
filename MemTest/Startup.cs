using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemTest
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
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseMvc();

            _memSinkTimer = new Timer(MemSink, null, 1, -1);
        }

        private readonly Random _random = new Random();
        private const int NumClients = 10;
        private static readonly HttpClientHandler HttpClientHandler = new HttpClientHandler();
        private readonly HttpClient[] _clients = Enumerable.Range(0, 10).Select(a => new HttpClient(new HttpClientHandler()) { BaseAddress = new Uri($"https://localhost:44375/api/values") }).ToArray();
        private static readonly HttpClient Client = new HttpClient(new SocketsHttpHandler()) {BaseAddress = new Uri("https://localhost:44375/")};
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private void MemSink(object state)
        {
            DoIt(1);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            Task.WaitAll(Task.Delay(TimeSpan.FromMinutes(1)));
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            //baseline memory snapshot here
            DoIt(10);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            Thread.Sleep(5000);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            Task.WaitAll(Task.Delay(TimeSpan.FromMinutes(5)));
            
            //second memory snapshot here
            while (true)
            {
                GC.WaitForPendingFinalizers();
                Task.WaitAll(Task.Delay(500));
            }
        }

        private static void DoIt(int times)
        {
            var tasks = new List<Task>();
            for (int loop = 0; loop < times; loop++)
            {
                tasks.Add(SimulateStupidLibrary());
            }
            Task.WaitAll(tasks.ToArray());
        }

        private static Task SimulateStupidLibrary()
        {
            var httpClient = new HttpClient() {BaseAddress = new Uri($"https://localhost:44375/")};
            var tasks2 = httpClient.GetAsync("api/values");
            return tasks2;
        }

        private void MemSink2(object state)
        {
            //pause for 5s after every 3 minutes of processing to let things stabilise

            //ramps up to 138.9MB ram
            while (true)
            {
                var tasks = new List<Task>();
                for (int i = 0; i < 10; i++)
                {
                    var tasks2 = _clients.Select(a =>
                        a.PutAsync("", new ObjectContent<string>("foo", new JsonMediaTypeFormatter())));
                    //var task = Client.PostAsync("api/values", new ObjectContent<string>("foo", new JsonMediaTypeFormatter()));
                    //tasks.Add(task);
                    tasks.AddRange(tasks2);
                }

                Task.WaitAll(tasks.ToArray());
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                Thread.Sleep(5000);
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                Thread.Sleep(5000);
            }

        }

        private int _numActiveRequests = 0;
        private async Task HttpRequest(HttpClient httpClient)
        {
            Interlocked.Increment(ref _numActiveRequests);
            var result = await httpClient.PostAsync("api/values", new ObjectContent(typeof(string), "cat", new JsonMediaTypeFormatter()), _cancellationTokenSource.Token);
            result.Dispose();
            Interlocked.Decrement(ref _numActiveRequests);
        }


        private Timer _memSinkTimer;
    }
}
