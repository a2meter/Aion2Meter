namespace INGMeter.App;

internal sealed class RdpsBuffAccumulator
{
	public RdpsBuffWindow Window { get; }

	public double AdditionalDamage { get; set; }

	public double ReducedDamage { get; set; }

	public RdpsBuffAccumulator(RdpsBuffWindow window)
	{
		Window = window;
	}
}
