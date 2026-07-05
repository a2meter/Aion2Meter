namespace INGMeter.App;

public class RdpsBuffRow
{
	public string IconPath { get; set; } = "";

	public string Name { get; set; } = "";

	public string ProviderName { get; set; } = "";

	public string TargetName { get; set; } = "";

	public string EffectText { get; set; } = "";

	public double AdditionalDps { get; set; }

	public double ReducedDps { get; set; }

	public double NetDps { get; set; }

	public string AdditionalDpsText { get; set; } = "";

	public string ReducedDpsText { get; set; } = "";

	public string NetDpsText { get; set; } = "";

	public string EvidenceText { get; set; } = "";
}
