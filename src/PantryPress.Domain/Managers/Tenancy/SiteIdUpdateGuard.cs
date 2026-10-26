using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// Refuses a bulk update that sets <see cref="ISiteOwned.SiteId"/> (PP2.5). <c>ExecuteUpdate</c> never passes through a
/// save, so <see cref="SiteStampInterceptor"/> cannot see it; the site filter keeps it to the context's own rows, but
/// nothing else would stop it moving them to another site. Checked when the query is compiled, which is before it ever
/// runs, and a refused query is never cached. Stateless.
/// </summary>
internal sealed class SiteIdUpdateGuard : IQueryExpressionInterceptor
{
    public static SiteIdUpdateGuard Instance { get; } = new();

    private SiteIdUpdateGuard()
    {
    }

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        new ExecuteUpdateVisitor().Visit(queryExpression);
        return queryExpression;
    }

    private sealed class ExecuteUpdateVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && node.Method.Name == nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)
                && node.Arguments.Skip(1).Any(SetsSiteId))
            {
                throw new InvalidOperationException("A bulk update never sets a site-owned row's site; a row never moves between sites.");
            }

            return base.VisitMethodCall(node);
        }

        // The setters arrive as pairs of a property selector and a value. A lambda whose whole body is a site-owned entity's
        // SiteId - by member or by EF.Property, on the lambda's entity or on one reached through a projection - is taken
        // for a selector. A value lambda of that shape would be refused too; copying a site into another column is not
        // something a site-owned type has reason to do.
        private static bool SetsSiteId(Expression setters) => new SelectorFinder().Finds(setters);
    }

    private sealed class SelectorFinder : ExpressionVisitor
    {
        private bool _found;

        public bool Finds(Expression expression)
        {
            Visit(expression);
            return _found;
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            if (node.Parameters.Count == 1 && NamesSiteId(Unwrap(node.Body)))
            {
                _found = true;
            }

            return base.VisitLambda(node);
        }

        private static bool NamesSiteId(Expression? body) => body switch
        {
            MemberExpression { Member.Name: nameof(ISiteOwned.SiteId), Expression: { } target } => IsSiteOwned(Unwrap(target)),
            MethodCallExpression { Method.Name: nameof(EF.Property), Arguments: [var target, ConstantExpression { Value: nameof(ISiteOwned.SiteId) }] } call
                when call.Method.DeclaringType == typeof(EF) => IsSiteOwned(Unwrap(target)),
            _ => false,
        };

        private static bool IsSiteOwned(Expression? target) => target is not null && typeof(ISiteOwned).IsAssignableFrom(target.Type);

        private static Expression? Unwrap(Expression? expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote } unary)
            {
                expression = unary.Operand;
            }

            return expression;
        }
    }
}
