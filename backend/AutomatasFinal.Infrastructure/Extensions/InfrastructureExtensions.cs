using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AutomatasFinal.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "No se encontró la cadena de conexión 'DefaultConnection'.");
        }

        var dataSourceBuilder =
            new NpgsqlDataSourceBuilder(connectionString);

        dataSourceBuilder.MapEnum<NotificationMethod>(
                "notification_method");

        dataSourceBuilder.MapEnum<AnalysisLanguage>(
            "analysis_language",
            new Npgsql.NameTranslation.NpgsqlNullNameTranslator());

        dataSourceBuilder.MapEnum<AnalysisStatus>(
            "analysis_status",
            new Npgsql.NameTranslation.NpgsqlNullNameTranslator());

        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<AppDbContext>(options =>  
        {
            options.UseNpgsql(dataSource);
        });

        return services;
    }
}