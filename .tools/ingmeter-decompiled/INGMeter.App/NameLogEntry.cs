namespace INGMeter.App;

public class NameLogEntry
{
	public string Time { get; set; } = "";

	public int ActorId { get; set; }

	public string Name { get; set; } = "";

	public string Source { get; set; } = "";

	public byte[]? RawPacket { get; set; }
}
