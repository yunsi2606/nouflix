using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nest;
using NouFlix.Adapters;
using NouFlix.DTOs;
using NouFlix.Persistence.Data;
using NouFlix.Persistence.Repositories;
using NouFlix.Persistence.Repositories.Interfaces;
using NouFlix.Services;
using NouFlix.Services.Backgroud;
using NouFlix.Services.Interface;
using NouFlix.Middlewares;
using NouFlix.Services.Payment;

namespace NouFlix.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var esUri = new Uri(configuration["Elasticsearch:Url"] ?? "http://localhost:9200");

        var settings = new ConnectionSettings(esUri)
            .DefaultIndex("audit-logs-*")
            .EnableDebugMode(); // optional: giúp xem request ES

        services.AddSingleton(new ElasticClient(settings));
        
        services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(
                    connectionString: configuration["ConnectionStrings:Default"],
                    sqlServerOptionsAction: sqlOptions =>
                    {
                        sqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(30),
                            errorNumbersToAdd: null);
                    }),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                // tự động gắn traceId nếu chưa có
                ctx.ProblemDetails.Extensions.TryAdd("traceId",
                    Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
            };
        });
        
        services.AddStackExchangeRedisCache(o =>
        {
            o.Configuration = configuration.GetConnectionString("Redis");
            o.InstanceName = "movies:"; // prefix key
        });
        
        services.AddHttpClient("origin").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        
        services.Configure<PaymentDto.MomoSettings>(configuration.GetSection("MoMo"));
        
        services.AddExceptionHandler<AppExceptionHandler>();
        
        services.AddHttpContextAccessor();
        
        services.AddScoped(typeof(Persistence.Repositories.Interfaces.IRepository<>), typeof(Repository<>));
        
        // Repositories
        services.AddScoped<IMovieRepository, MovieRepository>();
        services.AddScoped<IEpisodeRepository, EpisodeRepository>();
        services.AddScoped<IGenreRepository, GenreRepository>();
        services.AddScoped<IStudioRepository, StudioRepository>();
        services.AddScoped<IImageAssetRepository, ImageAssetRepository>();
        services.AddScoped<IVideoAssetRepository, VideoAssetRepository>();
        services.AddScoped<ISubtitleRepository, SubtitleRepository>();
        services.AddScoped<ISeasonRepository, SeasonRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IHistoryRepository, HistoryRepository>();
        services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
        services.AddScoped<IUserSubscriptionRepository, UserSubscriptionRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();

        // Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        
        // Adapters
        services.AddScoped<AspNetExternalTicketReader>();
        services.AddScoped<AuthUrlBuilder>();
        services.AddScoped<TokenCookieWriter>();
        services.AddScoped<ViewCounter>();
        
        // Services
        services.AddScoped<IMinioObjectStorage, MinioObjectStorage>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<AccessService>();
        services.AddScoped<StreamService>();
        services.AddScoped<IMovieService, MovieService>();
        services.AddScoped<SeasonService>();
        services.AddScoped<ExternalAuth>();
        services.AddScoped<FfmpegHlsTranscoder>();
        services.AddScoped<TaxonomyService>();
        services.AddScoped<AssetService>();
        services.AddScoped<EpisodeService>();
        services.AddScoped<SystemHealthService>();
        services.AddScoped<IMemoryCache, MemoryCache>();
        services.AddScoped<CsvService>();
        services.AddScoped<BulkEpisodesService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<LogService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<PaymentGatewayFactory>();
        services.AddHttpClient<IPaymentGateway, MomoPaymentGateway>();
        services.AddHttpClient<IPaymentGateway, StripePaymentGateway>();
        
        services.AddSingleton<IAppCache, DistributedCache>();
        
        var transcodeQ = new InMemoryQueue<TranscodeDto.TranscodeJob, TranscodeDto.TranscodeStatus>(
            jobStatus => jobStatus.JobId);
        services.AddSingleton<IQueue<TranscodeDto.TranscodeJob>>(transcodeQ);
        services.AddSingleton<IStatusStorage<TranscodeDto.TranscodeStatus>>(transcodeQ);
        
        var subQ = new InMemoryQueue<SubtitleDto.SubtitleJob, SubtitleDto.SubtitleStatus>(
            jobStatus => jobStatus.JobId);
        services.AddSingleton<IQueue<SubtitleDto.SubtitleJob>>(subQ);
        services.AddSingleton<IStatusStorage<SubtitleDto.SubtitleStatus>>(subQ);
        
        services.AddHostedService<TranscodeWorker>();
        services.AddHostedService<SubtitleWorker>();
        
        return services;
    }
    
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = true;
                options.SaveToken = false;
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        var authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
                        logger.LogDebug("JWT Header: {Auth}", authHeader);
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogError(ctx.Exception, "JWT failed");
                        return Task.CompletedTask;
                    }
                };
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!)),
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role
                };
            })
            .AddCookie("External")
            .AddGoogle("Google", options =>
            {
                options.ClientId = configuration["Auth:External:Google:ClientId"]!;
                options.ClientSecret = configuration["Auth:External:Google:ClientSecret"]!;
                options.CallbackPath = "/signin-google"; // đăng trong Google Console
                options.SignInScheme = "External";
                options.SaveTokens = true;
                
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                
                options.UserInformationEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";
                
                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "sub");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
                options.ClaimActions.MapJsonKey(ClaimTypes.GivenName, "given_name");
                options.ClaimActions.MapJsonKey(ClaimTypes.Surname, "family_name");
                options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
                options.ClaimActions.MapJsonKey("picture", "picture");
            })
            .AddFacebook("Facebook", options =>
            {
                options.ClientId = configuration["Auth:External:Facebook:ClientId"]!;
                options.ClientSecret = configuration["Auth:External:Facebook:ClientSecret"]!;
                options.CallbackPath = "/signin-facebook";
                options.SignInScheme = "External";
                options.SaveTokens = true;
                options.Scope.Add("email");
                options.Fields.Add("email");
            });
        
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(
                        "http://localhost:4200",
                        "http://localhost:4201",
                        "https://nouflix.nhatcuong.io.vn",
                        "http://localhost:5004",
                        "https://portal-nouflix.nhatcuong.io.vn")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }
    
    public static IServiceCollection AddSwagger(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSwaggerGen(c =>
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "MovieStream", Version = "v1" }));

        services.AddSwaggerGen(c =>
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "Please enter token",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                BearerFormat = "JWT",
                Scheme = "Bearer"
            }));
        services.AddSwaggerGen(c =>
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    []
                }
            }));

        return services;
    }

    public static IApplicationBuilder UseMiddlewares(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuditEnrichmentMiddleware>();
    }
}