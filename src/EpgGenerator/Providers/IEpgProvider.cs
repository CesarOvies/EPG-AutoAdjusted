namespace EpgGenerator.Providers;

public interface IEpgProvider
{
    string Name { get; }
    Task<List<ChannelData>> FetchAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default);
}

