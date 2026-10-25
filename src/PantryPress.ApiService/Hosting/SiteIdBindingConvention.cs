using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.ApiService.Hosting;

/// <summary>
/// Nothing a client sends can name a site (tenancy.md rule 7; PP2.4), checked when the host starts. An admin request's
/// site comes from its <c>{siteSlug}</c> and the caller's membership, a publishing request's from its key (PP18); so no
/// route value, query string, header, form field, or body may bind a <see cref="SiteId"/> or anything named a site id, at
/// any depth of a request type. A route that wants one fails the start instead of shipping.
/// </summary>
/// <remarks>
/// Names are compared by their letters alone, so <c>siteId</c>, <c>site_id</c>, <c>siteIds</c>, and <c>X-PP-Site-Id</c>
/// are one name. Request types are walked through their public properties, the types a polymorphic one names with
/// <see cref="JsonDerivedTypeAttribute"/>, arrays, and generic arguments - interface collections included, since a binder
/// fills those too - as far as PantryPress's own types go. Services and the framework's special parameters bind nothing
/// from the request and are not checked. An untyped body (<c>JsonElement</c>, <c>object</c>) is not seen through; a site
/// route takes a typed request.
/// </remarks>
internal sealed partial class SiteIdBindingConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var property in controller.ControllerProperties.Where(p => p.BindingInfo is not null))
            {
                Refuse($"{controller.ControllerType.Name}.{property.PropertyName}", property.BindingInfo, property.ParameterType, property.PropertyName);
            }

            foreach (var action in controller.Actions)
            {
                var name = $"{controller.ControllerType.Name}.{action.ActionMethod.Name}";
                foreach (var route in ApiSurfaceConvention.RouteTemplates(controller, action))
                {
                    if (RouteParameter().Matches(route).Select(m => m.Groups[1].Value).FirstOrDefault(NamesSiteId) is { } value)
                    {
                        throw new InvalidOperationException(
                            $"{name} is routed at '{route}', whose {{{value}}} would take a site id from the address. An admin "
                            + $"route names its site with {{{SiteAuthorization.SlugRouteValue}}}, and the membership resolves it.");
                    }
                }

                foreach (var parameter in action.Parameters)
                {
                    Refuse($"{name}({parameter.ParameterName})", parameter.BindingInfo, parameter.ParameterType, parameter.ParameterName);
                }
            }
        }
    }

    private static void Refuse(string where, BindingInfo? binding, Type type, string name)
    {
        if (binding?.BindingSource is { } source
            && (source.CanAcceptDataFrom(BindingSource.Services) || source.CanAcceptDataFrom(BindingSource.Special)))
        {
            return;
        }

        var carried = NamesSiteId(name) || NamesSiteId(binding?.BinderModelName)
            ? name
            : Carries(type, [], type.Name);
        if (carried is not null)
        {
            throw new InvalidOperationException(
                $"{where} binds a site id from the request ({carried}). A site is never taken from a body, query string, "
                + "header, or route value: an admin route's site comes from its slug and the caller's membership, and is read "
                + $"from {nameof(ISiteContext)}.");
        }
    }

    /// <summary>The path to a site id inside <paramref name="type"/>, or null when it carries none.</summary>
    private static string? Carries(Type type, HashSet<Type> seen, string path)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(SiteId))
        {
            return path;
        }

        if (!seen.Add(type))
        {
            return null;
        }

        if (type.IsArray)
        {
            return Carries(type.GetElementType()!, seen, path + "[]");
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (Carries(argument, seen, $"{path}<{argument.Name}>") is { } inner)
                {
                    return inner;
                }
            }
        }

        if (type.IsInterface || type.Assembly.GetName().Name?.StartsWith("PantryPress", StringComparison.Ordinal) != true)
        {
            return null;
        }

        foreach (var derived in type.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false))
        {
            if (Carries(derived.DerivedType, seen, $"{path}({derived.DerivedType.Name})") is { } inner)
            {
                return inner;
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var here = $"{path}.{property.Name}";
            // The property's own name, its JSON name, and the name a binder attribute gives it ([FromQuery(Name = …)],
            // [FromHeader(Name = …)], [ModelBinder(Name = …)]) inside a complex model.
            if (NamesSiteId(property.Name)
                || NamesSiteId(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
                || NamesSiteId(BindingInfo.GetBindingInfo(property.GetCustomAttributes())?.BinderModelName))
            {
                return here;
            }

            if (Carries(property.PropertyType, seen, here) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    private static bool NamesSiteId(string? name)
    {
        if (name is null)
        {
            return false;
        }

        var letters = new string([.. name.Where(char.IsAsciiLetter)]);
        return letters.EndsWith("siteid", StringComparison.OrdinalIgnoreCase)
            || letters.EndsWith("siteids", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\{\**([^}:=?]+)", RegexOptions.CultureInvariant)]
    private static partial Regex RouteParameter();
}
