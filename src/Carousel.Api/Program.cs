using System.Threading.RateLimiting;
using System.Text.Json;
using Carousel.Api.Endpoints;
using Carousel.Application;
using Carousel.Application.Abstractions;
using Carousel.Application.Services;
using Carousel.Application.Security;
using Carousel.Infrastructure;
using Carousel.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Локальные настройки разработчика (appsettings.{Env}.Local.json в .gitignore).
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.Local.json",
    optional: true,
    reloadOnChange: false);

// M3: trust X-Forwarded-Proto / X-Forwarded-For from reverse proxy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // XFF разбирает сам ForwardedHeadersMiddleware; альтернативы (Forwarded RFC 7239,
    // X-Real-IP) парсит наш RequestLogContextMiddleware — в ASP.NET Core их нет в enum.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // KnownIPNetworks/KnownProxies cleared — in containerized deploys the proxy IP is dynamic.
    // Restrict via firewall / Docker network instead.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext());

// Stars за публикации: fail-fast валидация (целое ≥1 и exempt-список) до Build/миграций.
try
{
    _ = Carousel.Application.Services.StarsBillingOptions.Validate(
        builder.Configuration["Billing:StarsPrice"],
        builder.Configuration.GetSection("Billing:FreeUsernames").GetChildren().Select(c => c.Value));
}
catch (InvalidOperationException ex)
{
    Log.Error(ex, "Невалидная конфигурация Billing — старт остановлен");
    throw;
}

// Администраторы по алиасам: fail-fast валидация до Build.
try
{
    _ = Carousel.Application.Services.AdminOptions.Validate(
        builder.Configuration.GetSection("Admin:Usernames").GetChildren().Select(c => c.Value));
}
catch (InvalidOperationException ex)
{
    Log.Error(ex, "Невалидная конфигурация Admin:Usernames — старт остановлен");
    throw;
}

// Retention-очистка БД: fail-fast валидация сроков/интервала до Build.
try
{
    var cleanupEnabled = bool.TryParse(builder.Configuration["Cleanup:Enabled"] ?? "true", out var e) && e;
    _ = Carousel.Application.Services.CleanupOptions.Validate(
        cleanupEnabled,
        builder.Configuration["Cleanup:PublicationRetentionDays"],
        builder.Configuration["Cleanup:OrphanMediaRetentionDays"],
        builder.Configuration["Cleanup:IntervalHours"]);
}
catch (InvalidOperationException ex)
{
    Log.Error(ex, "Невалидная конфигурация Cleanup — старт остановлен");
    throw;
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiter");
        logger.LogWarning(
            "Rate limit rejected {Method} {Path} from {RemoteIp}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            context.HttpContext.Connection.RemoteIpAddress);
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.AddPolicy("write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.AddPolicy("upload", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
            }));
});

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options => options.AddPolicy("miniapp", policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 20 * 1024 * 1024; // 20 MB
});

var app = builder.Build();

// M3: forwarded headers for reverse proxy (X-Forwarded-Proto → HTTPS detection).
app.UseForwardedHeaders();

// ClientIp + UserName в LogContext — до ExceptionHandler и request logging,
// чтобы все события запроса (включая 403/500) несли IP и никнейм.
app.UseMiddleware<Carousel.Api.Middleware.RequestLogContextMiddleware>();

// Request logging ВЫШЕ ExceptionHandler: иначе completion-событие видит дефолтный 500
// до выставления обработанного статуса (ложные [ERR] responded 500 на 4xx).
app.UseSerilogRequestLogging(o =>
{
    o.GetLevel = (ctx, _, ex) =>
        ex != null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error :
        ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning :
        LogEventLevel.Information;
    o.EnrichDiagnosticContext = (d, ctx) => d.Set("RequestId", ctx.TraceIdentifier);
});
app.UseExceptionHandler();

// M3: security headers on every response.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // API-only surface: no resources to load.
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
    await next();
});

app.UseCors("miniapp");
app.UseRateLimiter();

// M3: HTTPS redirect + HSTS in Production (Development runs plain HTTP on localhost).
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// H6: auto-migrate only in Development or when explicitly opted-in via config.
// Production deployments should run migrations as a separate one-off job.
var autoMigrate = app.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment());
if (autoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CarouselDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
    await Carousel.Infrastructure.Templates.TemplateSeeder.SeedAsync(
        db,
        app.Logger);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHealthChecks("/health/ready");

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapPublicationEndpoints();
app.MapMediaEndpoints();
// «Другие площадки» (platform accounts / Instagram) отключены — будет отдельной функциональностью.
// Код endpoints и адаптеров сохранён: MapPlatformAccountEndpoints, InstagramMockEndpoints.
app.MapBillingEndpoints();
app.MapTemplateEndpoints();
app.MapTelegramChatEndpoints();
app.MapAdminEndpoints();

if (app.Environment.IsDevelopment() && app.Services.GetService<Carousel.Infrastructure.Bot.MockTelegramBotApi>() is not null)
{
    app.MapTelegramMockEndpoints();
}

if (app.Environment.IsDevelopment())
{
    // Имитация оплаты Stars для dev/тестов (POST /api/dev/billing/stars-paid).
    app.MapBillingMockEndpoints();
}

app.Run();

public partial class Program;

public sealed class GlobalExceptionHandler : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // M2: only known business exceptions expose Message to the client.
        // InvalidOperationException and unknown types get a generic message —
        // EF/JSON internals in Message can leak entity names, tracking state.
        var (status, title, detail) = exception switch
        {
            UnauthorizedException e => (StatusCodes.Status401Unauthorized, e.Message, e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message, e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message, e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Message, e.Message),
            LimitExceededException e => (StatusCodes.Status403Forbidden, e.Message, e.Message),
            SubscriptionRequiredException e => (StatusCodes.Status403Forbidden, e.Message, e.Message),
            Carousel.Application.Security.ValidationException e => (StatusCodes.Status400BadRequest, e.Message, e.Message),
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid operation.", (string?)null),
            FluentValidation.ValidationException => (StatusCodes.Status400BadRequest, "Validation failed.", (string?)null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", (string?)null),
        };

        if (status == StatusCodes.Status403Forbidden)
        {
            await LogAccessDeniedAsync(httpContext, exception, cancellationToken);
        }
        else if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Path}", httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request failed {Status} for {Path}: {Message}",
                status, httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Extensions = { ["traceId"] = httpContext.TraceIdentifier },
        }, cancellationToken);

        return true;
    }

    private async Task LogAccessDeniedAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var userIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var resource = httpContext.Request.Path;

        string login = "anonymous";
        string roles = "-";
        Guid? userId = null;
        Guid? tenantId = null;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserContext>();
        var tenantAccess = scope.ServiceProvider.GetRequiredService<ITenantAccessService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        try
        {
            tenantId = currentUser.TenantId;
            var user = await currentUser.GetCurrentUserAsync(cancellationToken);
            if (user is not null)
            {
                userId = user.UserId;
                login = user.TelegramUsername ?? user.TelegramUserId.ToString();
                var memberships = await tenantAccess.GetMembershipsAsync(user.UserId, cancellationToken);
                roles = memberships.Count == 0
                    ? "-"
                    : string.Join(",", memberships.Select(m => $"{m.TenantId}:{m.Role}"));
            }
        }
        catch (Exception ex)
        {
            // Never fail the 403 response path because audit enrichment failed.
            _logger.LogWarning(ex, "Failed to resolve user context for ACCESS_DENIED on {Path}", resource);
        }

        _logger.LogWarning(
            "ACCESS_DENIED: пользователь {Login} (роли: {Roles}) попытался обратиться к {Resource}",
            login, roles, resource);

        try
        {
            await audit.WriteAsync(new AuditEntry(
                ActionCode: "ACCESS:API_DENIED",
                TenantId: tenantId,
                UserId: userId,
                Login: login,
                UserIp: userIp,
                Result: "DENIED",
                Details: new { Path = resource.ToString(), Method = httpContext.Request.Method, Reason = exception.Message }),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write ACCESS:API_DENIED audit for {Path}", resource);
        }
    }
}

public sealed class ValidationExceptionHandler : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ValidationExceptionHandler> _logger;

    public ValidationExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ValidationExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not FluentValidation.ValidationException validationException)
        {
            return false;
        }

        var propertyNames = validationException.Errors
            .Select(e => e.PropertyName)
            .Distinct()
            .ToArray();
        _logger.LogWarning("Validation failed for {Path}: {Properties}",
            httpContext.Request.Path, string.Join(", ", propertyNames));

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = validationException,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed.",
                Extensions =
                {
                    ["errors"] = validationException.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                    ["traceId"] = httpContext.TraceIdentifier,
                },
            },
        });
    }
}
