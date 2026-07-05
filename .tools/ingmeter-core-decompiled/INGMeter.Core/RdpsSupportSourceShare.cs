namespace INGMeter.Core;

public sealed record RdpsSupportSourceShare<TWindow>(TWindow Window, double Share) where TWindow : IRdpsSupportWindow;
