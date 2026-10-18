using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Security;
using Carousel.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ValidationException = Carousel.Application.Security.ValidationException;

namespace Carousel.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/auth/telegram", async (
            AuthTelegramRequest request,
            ITelegramAuthService auth,
            IJwtTokenService jwt,
            IValidator<AuthTelegramRequest> validator,
            IAuditService audit,
            CarouselDbContext db,
            HttpContext httpContext,
            Carousel.Application.Services.AdminOptions adminOptions,
            CancellationToken ct) =>
        {
            await validator.ValidateAndThrowAsync(request, ct);

            var userIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            TelegramAuthResult result;
            try
            {
                result = await auth.AuthenticateAsync(request.InitData, ct);
            }
            catch (ValidationException ex)
            {
                await WriteLoginFailedAsync(audit, userIp, ex.Message, ct);
                throw;
            }
            catch (InvalidOperationException ex)
            {
                await WriteLoginFailedAsync(audit, userIp, ex.Message, ct);
                throw new ValidationException(ex.Message);
            }

            var user = await db.Users.FirstAsync(u => u.TelegramUserId == result.TelegramUserId, ct);
            var token = jwt.CreateToken(user.Id, user.TelegramUserId);

            await audit.WriteAsync(new AuditEntry(
                ActionCode: "AUTH:LOGIN_SUCCESS",
                UserId: user.Id,
                Login: user.TelegramUsername ?? user.TelegramUserId.ToString(),
                UserIp: userIp,
                Result: "SUCCESS",
                Details: new { user.TelegramUserId }), ct);

            return Results.Ok(new AuthResponse(
                token,
                new UserDto(
                    user.Id,
                    user.TelegramUserId,
                    user.TelegramUsername,
                    user.DisplayName,
                    adminOptions.IsAdmin(user.TelegramUsername))));
        }).RequireRateLimiting("auth");

        api.MapGet("/me", async (ICurrentUserContext context, Carousel.Application.Services.AdminOptions adminOptions, CancellationToken ct) =>
        {
            var user = await context.RequireUserAsync(ct);
            return Results.Ok(new UserDto(
                user.UserId,
                user.TelegramUserId,
                user.TelegramUsername,
                user.DisplayName,
                adminOptions.IsAdmin(user.TelegramUsername)));
        });

        return app;
    }

    private static async Task WriteLoginFailedAsync(IAuditService audit, string userIp, string reason, CancellationToken ct)
    {
        await audit.WriteAsync(new AuditEntry(
            ActionCode: "AUTH:LOGIN_FAILED",
            Login: "anonymous",
            UserIp: userIp,
            Result: "FAILED",
            Details: new { Reason = reason }), ct);
    }
}
