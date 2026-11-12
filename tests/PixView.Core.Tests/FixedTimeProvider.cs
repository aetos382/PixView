namespace PixView.Core.Tests;

/// <summary>
/// 決まった現在時刻と、端末のタイムゾーンを返す <see cref="TimeProvider"/>。
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localTimeZone) : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset utcNow)
        : this(utcNow, TimeZoneInfo.Utc)
    {
    }

    public override TimeZoneInfo LocalTimeZone => localTimeZone;

    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
}
