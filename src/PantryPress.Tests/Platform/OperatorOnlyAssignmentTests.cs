using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Modules.Platform;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Tests.Api;

namespace PantryPress.Tests.Platform;

// PP1.4 restriction: there is no self-service path to PlatformAdmin. The role is assigned by the operator's one-shot
// migration job and nowhere else, so the request-serving hosts cannot even construct the facades that change it
// (ACCT-005).
public sealed class OperatorOnlyAssignmentTests
{
    private static readonly Type[] AssignmentServices =
        [typeof(IPlatformAdminFacade), typeof(IPlatformAdminReconciler), typeof(IAccountRoles)];

    [Fact]
    public async Task The_admin_API_cannot_resolve_anything_that_assigns_the_role()
    {
        await using var factory = ApiServiceFactory.WithoutDatabase();

        Assert.All(AssignmentServices, service => Assert.Null(factory.Services.GetService(service)));
    }

    [Fact]
    public void No_controller_or_service_in_the_admin_API_asks_for_one()
    {
        var api = typeof(ApiService.Controllers.AccountController).Assembly;

        var users = api.GetTypes()
            .SelectMany(type => type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(member => UsesAnAssignmentService(member))
            .Select(member => $"{member.DeclaringType?.Name}.{member.Name}");

        Assert.Empty(users);
    }

    [Fact]
    public void The_migration_job_is_the_one_host_that_registers_the_path()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformAdministration(new ConfigurationBuilder().Build());

        Assert.All(AssignmentServices, service => Assert.Contains(services, d => d.ServiceType == service));
    }

    private static bool UsesAnAssignmentService(MemberInfo member) => member switch
    {
        ConstructorInfo constructor => constructor.GetParameters().Any(p => AssignmentServices.Contains(p.ParameterType)),
        MethodInfo method => AssignmentServices.Contains(method.ReturnType)
            || method.GetParameters().Any(p => AssignmentServices.Contains(p.ParameterType)),
        FieldInfo field => AssignmentServices.Contains(field.FieldType),
        PropertyInfo property => AssignmentServices.Contains(property.PropertyType),
        _ => false,
    };
}
