namespace Namter.GameData;

public interface IGameDataTransport
{
    ValueTask<Stream> OpenReadAsync(Uri uri, CancellationToken cancellationToken);
}
