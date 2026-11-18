namespace Wms.Application.Demo;

/// <summary>Секция конфигурации <c>Demo</c>. По умолчанию демо выключено, ночного сброса нет.</summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }
    public int TokenLifetimeMinutes { get; set; } = 120;
    public int ResetHourMoscow { get; set; } = 3;
}
