using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PantryPress.Domain.Managers.Persistence;

/// <summary>
/// Keeps <see cref="IAppendOnly"/> rows as they were written (PP4.4): the context calls <see cref="EnsureUnchanged"/> before
/// every save, and the guard refuses a bulk update or delete of a marked type when the query is compiled, before it ever
/// runs, whichever site's rows it would reach. Raw SQL is not seen here; each marked table's trigger refuses it. Stateless.
/// The audit log keeps its own guard (PP1.11), which predates this one.
/// </summary>
internal sealed class AppendOnlyGuard : IQueryExpressionInterceptor
{
    internal const string Refusal = "This row is append-only: once written, it is never changed or removed.";

    public static AppendOnlyGuard Instance { get; } = new();

    private AppendOnlyGuard()
    {
    }

    /// <summary>
    /// Refuses a save that would change or remove an append-only row. Reading the entries runs change detection, so a
    /// property edited in place is caught.
    /// </summary>
    internal static void EnsureUnchanged(ChangeTracker changeTracker)
    {
        if (changeTracker.Entries<IAppendOnly>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(Refusal);
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
                && typeof(IAppendOnly).IsAssignableFrom(node.Method.GetGenericArguments()[0]))
            {
                throw new InvalidOperationException(Refusal);
            }

            return base.VisitMethodCall(node);
        }
    }
}
