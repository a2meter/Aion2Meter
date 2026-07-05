using System.Windows;

namespace INGMeter.App;

public static class MeterFontWeights
{
	public static FontWeight Text(MeterFontWeightMode mode)
	{
		return mode switch
		{
			MeterFontWeightMode.Light => FontWeights.Normal, 
			MeterFontWeightMode.Bold => FontWeights.Bold, 
			MeterFontWeightMode.ExtraBold => FontWeights.ExtraBold, 
			_ => FontWeights.SemiBold, 
		};
	}

	public static FontWeight Strong(MeterFontWeightMode mode)
	{
		return mode switch
		{
			MeterFontWeightMode.Light => FontWeights.SemiBold, 
			MeterFontWeightMode.Bold => FontWeights.ExtraBold, 
			MeterFontWeightMode.ExtraBold => FontWeights.Black, 
			_ => FontWeights.Bold, 
		};
	}
}
