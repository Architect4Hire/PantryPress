using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PantryPress.Domain.Managers.Persistence;

/// <summary>
/// Keeps <see cref="IReferenceData"/> read-only to the app (PP4.1): a migration seeds it, and nothing else writes it. The
/// context calls <see cref="EnsureUnchanged"/> before every save, and the guard refuses a bulk update or delete of a
/// reference type when the query is compiled, before it ever runs. Raw SQL is not seen here; it stays in repositories and
/// migrations, and a boundary test keeps each reference table to its own module. Stateless.
/// </summary>
internal sealed class ReferenceDataGuard : IQueryExpressionInterceptor
{
    private const string ReadOnly = "Platform reference data is read-only: it changes only through a reviewed migration.";

    public static ReferenceDataGuard Instance { get; } = new();

    private ReferenceDataGuard()
    {
    }

    /// <summary>
    /// Refuses a save that would add, change, or remove a reference row. Reading the entries runs change detection, so a
    /// property edited in place is caught.
    /// </summary>
    internal static void EnsureUnchanged(ChangeTracker changeTracker)
    {
        if (changeTracker.Entries<IReferenceData>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(ReadOnly);
        }
    }

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        new BulkWriteVisitor().Visit(queryExpression);
        return queryExpression;
    }

    private sealed class BulkWriteVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && node.Method.Name is nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)
                    or nameof(EntityFrameworkQueryableExtensions.ExecuteDelete)
                && node.Method.IsGenericMethod
                && typeof(IReferenceData).IsAssignableFrom(node.Method.GetGenericArguments()[0]))
            {
                throw new InvalidOperationException(ReadOnly);
            }

            return base.VisitMethodCall(node);
        }
    }
}
