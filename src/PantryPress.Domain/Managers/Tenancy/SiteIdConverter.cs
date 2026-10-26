using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>Stores a <see cref="SiteId"/> as its <see cref="Guid"/>, for every property of that type (PP2.5).</summary>
internal sealed class SiteIdConverter() : ValueConverter<SiteId, Guid>(id => id.Value, value => new SiteId(value));
