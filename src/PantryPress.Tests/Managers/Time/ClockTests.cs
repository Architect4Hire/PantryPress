using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Domain.Managers.Time;

namespace PantryPress.Tests.Managers.Time;

// PP0.5: the application clock (backend.md).
public sealed class ClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 8, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Fixed_clock_returns_exactly_the_fixed_instant_and_does_not_move_on_its_own()
    {
        var clock = new TimeProviderClock(new FakeTimeProvider(Start));

        Assert.Equal(Start, clock.UtcNow);
        Assert.Equal(Start, clock.UtcNow);
    }

    [Fact]
    public void Clock_moves_only_when_the_time_provider_advances()
    {
        var time = new FakeTimeProvider(Start);
        var clock = new TimeProviderClock(time);

        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(Start.AddMinutes(5), clock.UtcNow);
    }

    [Fact]
    public void UtcNow_always_has_a_zero_offset()
    {
        var chicago = new DateTimeOffset(2026, 3, 8, 3, 30, 0, TimeSpan.FromHours(-5));
        var clock = new TimeProviderClock(new OffsetTimeProvider(chicago));

        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
        Assert.Equal(chicago.UtcTicks, clock.UtcNow.UtcTicks);
    }

    [Fact]
    public void AddApplicationClock_keeps_a_time_provider_registered_first()
    {
        var time = new FakeTimeProvider(Start);
        var services = new ServiceCollection().AddSingleton<TimeProvider>(time).AddApplicationClock();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.Same(time, provider.GetRequiredService<TimeProvider>());
        Assert.Equal(Start, provider.GetRequiredService<IClock>().UtcNow);
        Assert.IsType<TzdbTimeZoneService>(provider.GetRequiredService<ITimeZoneService>());
    }

    [Fact]
    public void AddApplicationClock_registers_singletons()
    {
        using var provider = new ServiceCollection().AddApplicationClock().BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IClock>(), provider.GetRequiredService<IClock>());
        Assert.Same(provider.GetRequiredService<ITimeZoneService>(), provider.GetRequiredService<ITimeZoneService>());
    }

    private sealed class OffsetTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
