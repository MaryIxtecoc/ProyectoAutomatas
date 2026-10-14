using AutomatasFinal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutomatasFinal.API.Controllers;

/// <summary>
/// Estadísticas generales del sistema (no por usuario individual, para eso
/// está /api/users). Pensado para el dashboard de SUPERVISOR/ADMIN.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "ADMIN,SUPERVISOR")]
public class StatsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StatsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        var usersByRole = await _db.Users
            .Where(x => x.DeletedAt == null)
            .GroupBy(x => x.Role.Code)
            .Select(g => new { role = g.Key, count = g.Count() })
            .ToListAsync();

        var totalUsers = usersByRole.Sum(x => x.count);
        var activeUsers = await _db.Users
            .CountAsync(x => x.DeletedAt == null && x.IsActive);

        var analysesByLanguage = await _db.AnalysisJobs
            .GroupBy(x => x.Language)
            .Select(g => new { language = g.Key.ToString(), count = g.Count() })
            .ToListAsync();

        var analysesByStatus = await _db.AnalysisJobs
            .GroupBy(x => x.Status)
            .Select(g => new { status = g.Key.ToString(), count = g.Count() })
            .ToListAsync();

        var totalAnalyses = analysesByStatus.Sum(x => x.count);

        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
        var analysesLast7Days = await _db.AnalysisJobs
            .CountAsync(x => x.CreatedAt >= sevenDaysAgo);

        var recentAnalyses = await _db.AnalysisJobs
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new
            {
                x.Id,
                x.FileName,
                x.Language,
                x.Status,
                x.CreatedAt,
                Nickname = x.User.Nickname
            })
            .ToListAsync();

        var recentUsers = await _db.Users
            .Where(x => x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new
            {
                x.Id,
                x.Nickname,
                x.Email,
                RoleName = x.Role.Name,
                x.CreatedAt
            })
            .ToListAsync();

        return Ok(new
        {
            totalUsers,
            activeUsers,
            usersByRole,
            totalAnalyses,
            analysesLast7Days,
            analysesByLanguage,
            analysesByStatus,
            recentAnalyses,
            recentUsers
        });
    }
}
