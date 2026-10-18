using Carousel.Application.Abstractions;
using Carousel.Application.DTOs;
using Carousel.Application.Services;
using Carousel.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Carousel.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ILimitService, LimitService>();
        services.AddScoped<IMediaValidationService, MediaValidationService>();
        services.AddValidatorsFromAssemblyContaining<CreateTenantRequestValidator>();
        return services;
    }
}
