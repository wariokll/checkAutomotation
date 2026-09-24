using System.Configuration;
using System.Data;
using System.Windows;
using UIAutomationInspectorWpf.Services;

namespace UIAutomationInspectorWpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		if (!BuildExpirationService.IsBuildValid())
		{
			Shutdown(1);
			return;
		}

		new MainWindow().Show();
	}
}

