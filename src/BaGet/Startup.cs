using System;
using Amazon.Runtime.Internal;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Google.Apis.Requests.BatchRequest;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Threading;
using System.Buffers.Text;
using System.Net;
using System.Web.Http;

namespace BaGet
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        private IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // TODO: Ideally we'd use:
            //
            //       services.ConfigureOptions<ConfigureBaGetOptions>();
            //
            //       However, "ConfigureOptions" doesn't register validations as expected.
            //       We'll instead register all these configurations manually.
            // See: https://github.com/dotnet/runtime/issues/38491
            services.AddTransient<IConfigureOptions<CorsOptions>, ConfigureBaGetOptions>();
            services.AddTransient<IConfigureOptions<FormOptions>, ConfigureBaGetOptions>();
            services.AddTransient<IConfigureOptions<ForwardedHeadersOptions>, ConfigureBaGetOptions>();
            services.AddTransient<IConfigureOptions<IISServerOptions>, ConfigureBaGetOptions>();
            services.AddTransient<IValidateOptions<BaGetOptions>, ConfigureBaGetOptions>();

            services.AddBaGetOptions<IISServerOptions>(nameof(IISServerOptions));
            services.AddBaGetWebApplication(ConfigureBaGetApplication);

            // You can swap between implementations of subsystems like storage and search using BaGet's configuration.
            // Each subsystem's implementation has a provider that reads the configuration to determine if it should be
            // activated. BaGet will run through all its providers until it finds one that is active.
            services.AddScoped(DependencyInjectionExtensions.GetServiceFromProviders<IContext>);
            services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IStorageService>);
            services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IPackageDatabase>);
            services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchService>);
            services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchIndexer>);

            services.AddSingleton<IConfigureOptions<MvcRazorRuntimeCompilationOptions>, ConfigureRazorRuntimeCompilation>();

            services.AddCors();
        }

        private void ConfigureBaGetApplication(BaGetApplication app)
        {
            // Add database providers.
            app.AddAzureTableDatabase();
            app.AddMySqlDatabase();
            app.AddPostgreSqlDatabase();
            app.AddSqliteDatabase();
            app.AddSqlServerDatabase();

            // Add storage providers.
            app.AddFileStorage();
            app.AddAliyunOssStorage();
            app.AddAwsS3Storage();
            app.AddAzureBlobStorage();
            app.AddGoogleCloudStorage();

            // Add search providers.
            app.AddAzureSearch();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var username = Configuration.GetValue<string>("LoginName");
            var password = Configuration.GetValue<string>("Password");

            if(!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                app.Use(async (context, next) =>
                {
                    // Authenticate user before continuing request.
                    var parsed = AuthenticationHeaderValue.TryParse(context.Request.Headers["Authorization"], out var result);
                    var login = parsed ? Encoding.UTF8.GetString(Convert.FromBase64String(result.Parameter)) : string.Empty;

                    if (!login.Equals($"{username}:{password}"))
                    {
                        // Trigger basic authentication prompt.
                        context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                        context.Response.Headers.WWWAuthenticate = "Basic realm=\"IasonNuGet\"";
                        await context.Response.StartAsync();
                    }
                    else
                    {
                        await next.Invoke(context);
                    }
                });
            }

            var options = Configuration.Get<BaGetOptions>();

            app.Map(Configuration.GetValue<string>("BasePath"), mainapp =>
            {
                mainapp.UseForwardedHeaders();

                mainapp.UseStaticFiles();
                mainapp.UseRouting();

                mainapp.UseCors(ConfigureBaGetOptions.CorsPolicy);
                mainapp.UseOperationCancelledMiddleware();

                mainapp.UseEndpoints(endpoints =>
                {
                    var baget = new BaGetEndpointBuilder();

                    baget.MapEndpoints(endpoints);
                });
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseStatusCodePages();
            }
        }
    }

    //public class BasicAuthHttpModule : System.Web.IHttpModule
    //{
    //    private const string Realm = "My Realm";

    //    public void Init(HttpApplication context)
    //    {
    //        // Register event handlers
    //        context.AuthenticateRequest += OnApplicationAuthenticateRequest;
    //        context.EndRequest += OnApplicationEndRequest;
    //    }

    //    private static void SetPrincipal(IPrincipal principal)
    //    {
    //        Thread.CurrentPrincipal = principal;
    //        if (HttpContext.Current != null)
    //        {
    //            HttpContext.Current.User = principal;
    //        }
    //    }

    //    // TODO: Here is where you would validate the username and password.
    //    private static bool CheckPassword(string username, string password)
    //    {
    //        return username == "user" && password == "password";
    //    }

    //    private static void AuthenticateUser(string credentials)
    //    {
    //        try
    //        {
    //            var encoding = Encoding.GetEncoding("iso-8859-1");
    //            credentials = encoding.GetString(Convert.FromBase64String(credentials));

    //            int separator = credentials.IndexOf(':');
    //            string name = credentials.Substring(0, separator);
    //            string password = credentials.Substring(separator + 1);

    //            if (CheckPassword(name, password))
    //            {
    //                var identity = new GenericIdentity(name);
    //                SetPrincipal(new GenericPrincipal(identity, null));
    //            }
    //            else
    //            {
    //                // Invalid username or password.
    //                HttpContext.Current.Response.StatusCode = 401;
    //            }
    //        }
    //        catch (FormatException)
    //        {
    //            // Credentials were not formatted correctly.
    //            HttpContext.Current.Response.StatusCode = 401;
    //        }
    //    }

    //    private static void OnApplicationAuthenticateRequest(object sender, EventArgs e)
    //    {
    //        var request = HttpContext.Current.Request;
    //        var authHeader = request.Headers["Authorization"];
    //        if (authHeader != null)
    //        {
    //            var authHeaderVal = AuthenticationHeaderValue.Parse(authHeader);

    //            // RFC 2617 sec 1.2, "scheme" name is case-insensitive
    //            if (authHeaderVal.Scheme.Equals("basic",
    //                    StringComparison.OrdinalIgnoreCase) &&
    //                authHeaderVal.Parameter != null)
    //            {
    //                AuthenticateUser(authHeaderVal.Parameter);
    //            }
    //        }
    //    }

    //    // If the request was unauthorized, add the WWW-Authenticate header 
    //    // to the response.
    //    private static void OnApplicationEndRequest(object sender, EventArgs e)
    //    {
    //        var response = HttpContext.Current.Response;
    //        if (response.StatusCode == 401)
    //        {
    //            response.Headers.Add("WWW-Authenticate",
    //                string.Format("Basic realm=\"{0}\"", Realm));
    //        }
    //    }

    //    public void Dispose()
    //    {
    //    }
    //}
}
