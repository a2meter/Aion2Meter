using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using INGMeter.Core;

namespace INGMeter.App;

public class PacketDetailsWindow : Window, IComponentConnector
{
	private static readonly SolidColorBrush LenColor = new SolidColorBrush(Color.FromRgb(150, 150, 150));

	private static readonly SolidColorBrush OpColor = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 107, 107));

	private static readonly SolidColorBrush IdColor = new SolidColorBrush(Color.FromRgb(78, 204, 163));

	private static readonly SolidColorBrush SkillColor = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 217, 61));

	private static readonly SolidColorBrush DmgColor = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 158, 250));

	private static readonly SolidColorBrush TagColor = new SolidColorBrush(Color.FromRgb(168, 130, byte.MaxValue));

	private static readonly SolidColorBrush DefaultColor = new SolidColorBrush(Color.FromRgb(180, 180, 200));

	internal TextBlock txtTitle;

	internal TextBlock txtSummary;

	internal WrapPanel legendPanel;

	internal RichTextBox rtbHex;

	private bool _contentLoaded;

	public PacketDetailsWindow(string title, string summary, byte[]? data, int highlightSkill = 0, int highlightDmg = 0)
	{
		InitializeComponent();
		txtTitle.Text = title;
		txtSummary.Text = summary;
		if (data != null && data.Length != 0)
		{
			BuildHexDump(data, highlightSkill, highlightDmg);
		}
	}

	private void BuildHexDump(byte[] data, int hSkill, int hDmg)
	{
		Dictionary<int, Brush> dictionary = new Dictionary<int, Brush>();
		int num = 0;
		int value = 0;
		int length = 0;
		if (data.Length > 2 && (data[1] == 4 || data[1] == 5) && data[2] == 56)
		{
			value = data[0];
			length = 1;
		}
		else
		{
			VarInt.TryRead(data, num, out value, out length);
		}
		if (length > 0)
		{
			for (int i = 0; i < length; i++)
			{
				dictionary[num + i] = LenColor;
			}
			num += length;
		}
		bool flag = false;
		bool flag2 = false;
		if (num + 1 < data.Length)
		{
			byte b = data[num];
			byte b2 = data[num + 1];
			if ((b == 4 || b == 5) && b2 == 56)
			{
				flag = true;
				flag2 = b == 5;
			}
			dictionary[num] = OpColor;
			dictionary[num + 1] = OpColor;
			num += 2;
		}
		if (flag)
		{
			int value2;
			if (flag2)
			{
				if (VarInt.TryRead(data, num, out value2, out var length2))
				{
					for (int j = 0; j < length2; j++)
					{
						dictionary[num + j] = IdColor;
					}
					num += length2;
				}
				num++;
				if (VarInt.TryRead(data, num, out value2, out var length3))
				{
					for (int k = 0; k < length3; k++)
					{
						dictionary[num + k] = IdColor;
					}
					num += length3;
				}
			}
			else
			{
				if (VarInt.TryRead(data, num, out value2, out var length4))
				{
					for (int l = 0; l < length4; l++)
					{
						dictionary[num + l] = IdColor;
					}
					num += length4;
				}
				if (VarInt.TryRead(data, num, out value2, out var length5))
				{
					num += length5;
				}
				if (VarInt.TryRead(data, num, out value2, out var length6))
				{
					num += length6;
				}
				if (VarInt.TryRead(data, num, out value2, out var length7))
				{
					for (int m = 0; m < length7; m++)
					{
						dictionary[num + m] = IdColor;
					}
					num += length7;
				}
			}
			if (hSkill != 0)
			{
				for (int n = 0; n < 10 && num + n + 4 <= data.Length; n++)
				{
					if ((data[num + n] | (data[num + n + 1] << 8) | (data[num + n + 2] << 16) | (data[num + n + 3] << 24)) == hSkill)
					{
						for (int num2 = 0; num2 < 4; num2++)
						{
							dictionary[num + n + num2] = SkillColor;
						}
						num += n + 5;
						break;
					}
				}
			}
			if (hDmg != 0)
			{
				for (int num3 = 0; num3 < 20 && num + num3 < data.Length; num3++)
				{
					if (VarInt.TryRead(data, num + num3, out var value3, out var length8) && value3 == hDmg)
					{
						for (int num4 = 0; num4 < length8; num4++)
						{
							dictionary[num + num3 + num4] = DmgColor;
						}
						num += num3 + length8;
						break;
					}
				}
			}
			int val = length + value;
			while (num < Math.Min(data.Length, val))
			{
				dictionary[num++] = TagColor;
				if (!VarInt.TryRead(data, num, out value2, out var length9))
				{
					continue;
				}
				for (int num5 = 0; num5 < length9; num5++)
				{
					if (num + num5 < data.Length)
					{
						dictionary[num + num5] = TagColor;
					}
				}
				num += length9;
			}
		}
		else
		{
			for (int num6 = num; num6 < data.Length; num6++)
			{
				if (data[num6] == 54 && num6 + 1 < data.Length)
				{
					if (VarInt.TryRead(data, num6 + 1, out var value4, out var length10) && value4 >= 100)
					{
						dictionary[num6] = OpColor;
						for (int num7 = 0; num7 < length10; num7++)
						{
							dictionary[num6 + 1 + num7] = IdColor;
						}
						num6 += length10;
					}
				}
				else if (data[num6] == 7 && num6 + 1 < data.Length)
				{
					int num8 = data[num6 + 1];
					if (num8 > 0 && num8 <= 64 && num6 + 2 + num8 <= data.Length)
					{
						dictionary[num6] = OpColor;
						dictionary[num6 + 1] = LenColor;
						for (int num9 = 0; num9 < num8; num9++)
						{
							dictionary[num6 + 2 + num9] = SkillColor;
						}
						num6 += num8 + 1;
					}
				}
				else if (data[num6] == 248 && num6 + 1 < data.Length && data[num6 + 1] == 3)
				{
					dictionary[num6] = OpColor;
					dictionary[num6 + 1] = OpColor;
					num6++;
				}
			}
		}
		AddLegendItem("길이", LenColor);
		AddLegendItem("Op/태그", OpColor);
		AddLegendItem("ID", IdColor);
		AddLegendItem("스킬/이름", SkillColor);
		if (flag)
		{
			AddLegendItem("데미지", DmgColor);
			AddLegendItem("추가태그", TagColor);
		}
		Paragraph paragraph = new Paragraph();
		for (int num10 = 0; num10 < data.Length; num10 += 16)
		{
			int num11 = num10;
			paragraph.Inlines.Add(new Run(num11.ToString("X4") + ": ")
			{
				Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 150))
			});
			StringBuilder stringBuilder = new StringBuilder();
			for (int num12 = 0; num12 < 16; num12++)
			{
				int num13 = num11 + num12;
				if (num13 < data.Length)
				{
					string text = data[num13].ToString("X2") + " ";
					Brush value5;
					Brush foreground = (dictionary.TryGetValue(num13, out value5) ? value5 : DefaultColor);
					paragraph.Inlines.Add(new Run(text)
					{
						Foreground = foreground,
						FontWeight = (dictionary.ContainsKey(num13) ? FontWeights.Bold : FontWeights.Normal)
					});
					char value6 = (char)((data[num13] >= 32 && data[num13] < 127) ? data[num13] : 46);
					stringBuilder.Append(value6);
				}
				else
				{
					paragraph.Inlines.Add(new Run("   "));
				}
				if (num12 == 7)
				{
					paragraph.Inlines.Add(new Run(" "));
				}
			}
			paragraph.Inlines.Add(new Run("  " + stringBuilder.ToString())
			{
				Foreground = new SolidColorBrush(Color.FromRgb(120, 140, 160))
			});
			paragraph.Inlines.Add(new LineBreak());
		}
		rtbHex.Document.Blocks.Add(paragraph);
	}

	private void AddLegendItem(string text, Brush color)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 15.0, 0.0)
		};
		stackPanel.Children.Add(new Border
		{
			Width = 10.0,
			Height = 10.0,
			Background = color,
			Margin = new Thickness(0.0, 0.0, 5.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = text,
			Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
			FontSize = 11.0,
			VerticalAlignment = VerticalAlignment.Center
		});
		legendPanel.Children.Add(stackPanel);
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		DragMove();
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/packetdetailswindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			((Border)target).MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
			break;
		case 2:
			txtTitle = (TextBlock)target;
			break;
		case 3:
			((Button)target).Click += BtnClose_Click;
			break;
		case 4:
			txtSummary = (TextBlock)target;
			break;
		case 5:
			legendPanel = (WrapPanel)target;
			break;
		case 6:
			rtbHex = (RichTextBox)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
