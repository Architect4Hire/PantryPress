using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Email;

namespace PantryPress.Domain.Integrations.DevelopmentMail;

public static class DevelopmentMailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the development sink as the <see cref="IEmailGateway"/>. The options are checked at startup: a
    /// non-loopback host fails the host instead of sending mail.
    /// </summary>
    public static IServiceCollection AddDevelopmentMailSink(
        this IServiceCollection services,
        Action<DevelopmentMailOptions> configure)
    {
        services.AddOptions<DevelopmentMailOptions>()
            .Configure(configure)
            .Validate(
                options => options.IsValid(),
                "The development mail sink needs a loopback SMTP host, a port, a valid sender address, and a timeout.")
            .ValidateOnStart();
        services.AddSingleton<IEmailGateway, SmtpDevelopmentMailGateway>();
        return services;
    }
}
