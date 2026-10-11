// One virtual bridge push-link window. Times are UTC and never persisted;
// opening a pairing window requires an explicit local command-line flag.
public sealed class HuePairingWindow
{
    private readonly DateTimeOffset _expiresAt;

    public HuePairingWindow(int seconds, DateTimeOffset? startedAt = null)
    {
        if (seconds is < 0 or > 300)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        _expiresAt = (startedAt ?? DateTimeOffset.UtcNow).AddSeconds(seconds);
    }

    public bool IsPressed => IsPressedAt(DateTimeOffset.UtcNow);

    internal bool IsPressedAt(DateTimeOffset now) => now < _expiresAt;

    public static bool RunSelfTest()
    {
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var off = new HuePairingWindow(0, now);
        var on = new HuePairingWindow(120, now);
        return !off.IsPressedAt(now)
            && on.IsPressedAt(now)
            && on.IsPressedAt(now.AddSeconds(119))
            && !on.IsPressedAt(now.AddSeconds(120))
            && !on.IsPressedAt(now.AddSeconds(121));
    }
}
