using KrakowOpenData.Application.Abstractions;

namespace KrakowOpenData.Infrastructure.Common;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
