using Asp.Versioning.ApiExplorer;
using AspNetAPIFundamentals.DataContext;
using AspNetAPIFundamentals.Services;
using Azure.Identity;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Reflection;
using System.Text;

namespace AspNetAPIFundamentals
{
    public class Program
    {
        public static void Main(string[] args)
        {
            //adding serilog
            /*Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/cityinfo.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();*/

            Log.Logger = new LoggerConfiguration()
                             .MinimumLevel.Debug()
                             .WriteTo.Console()
                             .CreateLogger();

            var builder = WebApplication.CreateBuilder(args);

            //builder.Logging.ClearProviders();
            //builder.Logging.AddConsole();

            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (environment == Environments.Development)
            {
                builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
                        .MinimumLevel.Debug()
                        .WriteTo.Console());
            }
            else
            {
                /*var secretClient = new SecretClient(
                        new Uri("https://pluralsightdemokeyvault.vault.azure.net/"),
                        new DefaultAzureCredential());
                builder.Configuration.AddAzureKeyVault(secretClient,
                    new KeyVaultSecretManager());*/


                builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
                        .MinimumLevel.Debug()
                        .WriteTo.Console()
                        //.WriteTo.File("logs/cityinfo.txt", rollingInterval: RollingInterval.Day)
                        .WriteTo.ApplicationInsights(new TelemetryConfiguration
                        {
                            InstrumentationKey = builder.Configuration["ApplicationInsightsInstrumentationKey"]
                        }, TelemetryConverter.Traces));
            }

            // Add services to the container.

            builder.Services.AddControllers(options =>
            {
                //send error message when header type is not acceptable.
                //like we are sending response in json and cleint needs in xml
                options.ReturnHttpNotAcceptable = true;
            }).AddNewtonsoftJson().AddXmlDataContractSerializerFormatters(); //use this to add the support for xml now if client accepts in xml format

            builder.Services.AddProblemDetails();
            //manipulate the error responses
            /*builder.Services.AddProblemDetails(options =>
            {
                options.CustomizeProblemDetails = ctx =>
                {
                    ctx.ProblemDetails.Extensions.Add("Additional Indo", "Additional Info Example");
                    ctx.ProblemDetails.Extensions.Add("server", Environment.MachineName);
                };
            });*/

            builder.Services.AddSingleton<FileExtensionContentTypeProvider>();
#if DEBUG
            builder.Services.AddTransient<IMailService, LocalMailService>();
#else
            builder.Services.AddTransient<IMailService, CloudMailService>();
#endif
            builder.Services.AddSingleton<CitiesDataStore>();
            builder.Services.AddDbContext<CityInfoContext>
                (dbContextOptions => dbContextOptions
                .UseSqlServer(builder.Configuration["ConnectionStrings:DBConnectionString"]));

            builder.Services.AddScoped<ICityInfoRepository, CityInfoRepository>();
            builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

            builder.Services.AddAuthentication("Bearer")
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new()
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = builder.Configuration["Authentication:Issuer"],
                        ValidAudience = builder.Configuration["Authentication:Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.ASCII.GetBytes(builder.Configuration["Authentication:SecretForKey"]))
                    };
                });

            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("MustBeFromAntwerp", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("city", "Antwerp");
                });
            });

            builder.Services.AddApiVersioning(options =>
            {
                options.ReportApiVersions = true;
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
            }).AddMvc()
            .AddApiExplorer(options =>
            {
                options.SubstituteApiVersionInUrl = true;
            });

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            var apiVersionDescriptionProvider = builder.Services.BuildServiceProvider()
                .GetRequiredService<IApiVersionDescriptionProvider>();

            builder.Services.AddSwaggerGen(setupAction =>
            {
                foreach (var description in apiVersionDescriptionProvider.ApiVersionDescriptions)
                {
                    setupAction.SwaggerDoc($"{description.GroupName}",
                        new()
                        {
                            Title = "City Info API",
                            Version = description.ApiVersion.ToString(),
                            Description = "Through this API you can access cities and their points of interest."
                        });
                }

                //var xmlCommentsFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlCommentsFile = "apifundamentals.xml";
                var xmlCommentsFullPath = Path.Combine(AppContext.BaseDirectory, xmlCommentsFile);

                setupAction.IncludeXmlComments(xmlCommentsFullPath);

                setupAction.AddSecurityDefinition("ApiBearerAuth", new()
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    Description = "Input a valid token to access this API"
                });

                setupAction.AddSecurityRequirement(new()
                {
                    {
                        new ()
                        {
                            Reference = new OpenApiReference {
                                Type = ReferenceType.SecurityScheme,
                                Id = "ApiBearerAuth" }
                        },
                        new List<string>()
                    }
                });
            });

            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            });

            var app = builder.Build();

            app.UseForwardedHeaders();

            // Configure the HTTP request pipeline.
            app.UseSwagger();
            app.UseSwaggerUI(setupAction =>
            {
                var descriptions = app.DescribeApiVersions();
                foreach (var description in descriptions)
                {
                    setupAction.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
                        description.GroupName.ToUpperInvariant());
                }
            });
           

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            //app.Run();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            /*app.Run(async (context) =>
            {
                await context.Response.WriteAsync("Hello World");
            });*/
            app.Run();
        }
    }
}
