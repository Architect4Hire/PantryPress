using Microsoft.EntityFrameworkCore;

namespace PantryPress.Domain.Managers.Persistence;

/// <summary>
/// The one EF Core context for PantryPress (backend.md). The model is empty until the first module adds entities.
/// Migrations live in PantryPress.MigrationService and are applied only there (OPS-003).
/// </summary>
public sealed class PantryPressDbContext(DbContextOptions<PantryPressDbContext> options) : DbContext(options);
