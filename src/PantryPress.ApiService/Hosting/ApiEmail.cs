using PantryPress.Domain.Integrations.DevelopmentMail;
using PantryPress.Domain.Managers.Email;

namespace PantryPress.ApiService.Hosting;

internal static class ApiEmail
{
    /// <summary>
    /// The email queue, plus the development sink in local Development only (PP1.2a). No real provider exists until
    /// PP13.1, so any other environment registers no <see cref="IEmailGateway"/> and the host fails to start when the
    /// send worker asks for one, rather than silently dropping account email.
    /// </summary>
    public static IHostApplicationBuilder AddApiEmail(this IHostApplicationBuilder builder)
    {
        builder.Services.AddEmailQueue();
        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddDevelopmentMailSink(options => builder.Configuration.GetSection("DevelopmentMail").Bind(options));
        }

        return builder;
    }
}
