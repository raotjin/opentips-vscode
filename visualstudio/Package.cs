using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace OpenTips.VisualStudio {
 [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
 [InstalledProductRegistration("OpenTips", "Real-time code review", "0.1")]
 [ProvideMenuResource("Menus.ctmenu", 1)]
 [ProvideToolWindow(typeof(TipsWindow))]
 [Guid("6e3b2e95-902b-4385-a966-30c06ab3c7a6")]
 public sealed class Package : AsyncPackage {
  protected override async Task InitializeAsync(CancellationToken token, IProgress<ServiceProgressData> progress) {
   await JoinableTaskFactory.SwitchToMainThreadAsync(token);
   var commands = (IMenuCommandService)await GetServiceAsync(typeof(IMenuCommandService));
   commands.AddCommand(new MenuCommand((s,e) => JoinableTaskFactory.RunAsync(async () => await ShowToolWindowAsync(typeof(TipsWindow), 0, true, DisposalToken)), new CommandID(new Guid("9cc1062b-4c82-46d2-adcb-f5c17d55fb85"), 0x100)));
  }
 }
 [Guid("e4e2ba26-a455-4c53-adb3-8225fb696f8b")]
 public sealed class TipsWindow : ToolWindowPane {
  public TipsWindow() : base(null) { Caption = "OpenTips"; Content = new TipsControl(); }
 }
}
