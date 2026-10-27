using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EnvDTE;
using Process = System.Diagnostics.Process;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;

namespace OpenTips.VisualStudio {
 public sealed class TipsControl : UserControl {
  readonly StackPanel panel = new StackPanel { Margin = new Thickness(8) };
  readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };
  readonly ListBox tips = new ListBox { MinHeight = 150, DisplayMemberPath = "Label" };
  readonly PasswordBox key = new PasswordBox();
  readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
  readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
  readonly SemaphoreSlim polling = new SemaphoreSlim(1,1);
  Process process; int port; string root; FileSystemWatcher watcher;
  sealed class Tip { public string Id; public string File; public int Line; public string Description; public string Label { get; set; } }
  public TipsControl() {
   panel.Children.Add(new TextBlock { Text = "Anthropic API key (used for this session):" }); panel.Children.Add(key);
   panel.Children.Add(Button("Start review", Start)); panel.Children.Add(status); panel.Children.Add(tips);
   panel.Children.Add(Button("Open selected suggestion", OpenTip)); panel.Children.Add(Button("Dismiss selected suggestion", Dismiss));
   Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
   timer.Tick += async (s,e) => await Poll(); Unloaded += (s,e) => Stop();
   status.Text = "Open a Git solution, enter an Anthropic key, and start review.";
  }
  Button Button(string title, RoutedEventHandler handler) { var b = new Button { Content = title, Margin = new Thickness(0,4,0,4), Padding = new Thickness(6) }; b.Click += handler; return b; }
  async void Start(object sender, RoutedEventArgs e) {
   try {
    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(); Stop();
    var dte = (DTE)Package.GetGlobalService(typeof(DTE));
    var solution = dte?.Solution?.FullName;
    if (string.IsNullOrEmpty(solution)) throw new Exception("Open a saved solution first.");
    root = Path.GetDirectoryName(solution);
    if (string.IsNullOrWhiteSpace(key.Password)) throw new Exception("Enter an Anthropic API key.");
    // ponytail: default to the local checkout; set OPENTIPS_PYTHON for other installations.
    string python = Environment.GetEnvironmentVariable("OPENTIPS_PYTHON") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "opentips-visualstudio", ".venv", "Scripts", "python.exe");
    if (!File.Exists(python)) throw new Exception("OpenTips Python environment was not found: " + python);
    var ready = new TaskCompletionSource<int>();
    var psi = new ProcessStartInfo(python, "-u -m opentips.cli.main -p 0") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    psi.EnvironmentVariables["ANTHROPIC_API_KEY"] = key.Password;
    process = new Process { StartInfo = psi, EnableRaisingEvents = true };
    DataReceivedEventHandler read = (s,a) => { if (a.Data == null) return; var m = Regex.Match(a.Data, @"OpenTips running on\s+[^:\s]+:(\d+)"); if (m.Success) ready.TrySetResult(int.Parse(m.Groups[1].Value)); };
    process.OutputDataReceived += read; process.ErrorDataReceived += read; process.Exited += (s,a) => ready.TrySetException(new Exception("OpenTips service exited during startup."));
    process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); status.Text = "Starting OpenTips...";
    if (await Task.WhenAny(ready.Task, Task.Delay(30000)) != ready.Task) throw new Exception("OpenTips service did not start within 30 seconds.");
    port = await ready.Task;
    watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
    watcher.Changed += Changed; watcher.Created += Changed; watcher.Renamed += Changed; watcher.EnableRaisingEvents = true;
    timer.Start(); status.Text = "Reviewing saved changes in " + root; await Refresh();
   } catch (Exception ex) { Stop(); status.Text = ex.Message; }
  }
  void Changed(object sender, FileSystemEventArgs e) { if (e.FullPath.IndexOf(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0) return; _ = Notify(e.FullPath); }
  async Task Notify(string path) { try { await Rpc("changed", new JObject { ["file_names"] = new JArray(path) }); } catch (Exception ex) { await Dispatcher.InvokeAsync(() => status.Text = ex.Message); } }
  async Task<JToken> Rpc(string method, JObject args) {
   var request = new JObject { ["jsonrpc"] = "2.0", ["id"] = Guid.NewGuid().ToString(), ["method"] = method, ["params"] = args };
   using (var response = await http.PostAsync("http://localhost:" + port + "/rpc", new StringContent(request.ToString(), Encoding.UTF8, "application/json"))) {
    response.EnsureSuccessStatusCode(); var json = JObject.Parse(await response.Content.ReadAsStringAsync());
    if (json["error"] != null) throw new Exception(json["error"].ToString()); return json["result"];
   }
  }
  async Task Refresh() { var list = await Rpc("list_tips", new JObject()); Render(list); }
  void Render(JToken list) {
   if (!(list is JArray a)) return;
   tips.Items.Clear(); foreach (var t in a) tips.Items.Add(new Tip { Id = (string)t["id"], File = (string)t["file"], Line = (int?)t["line"] ?? 1, Description = (string)t["description"], Label = ((string)t["label"] ?? (string)t["description"]) + " — " + (string)t["file"] + ":" + (int?)t["line"] });
  }
  async Task Poll() {
   if (!await polling.WaitAsync(0)) return;
   try { if (process == null || process.HasExited) { Stop(); status.Text = "OpenTips service stopped."; return; }
    var events = await Rpc("poll_events", new JObject());
    foreach (var ev in events) { if ((string)ev["type"] == "tips") { var error = (string)ev["data"]?["error"]; if (!string.IsNullOrEmpty(error)) status.Text = error; else await Refresh(); } }
   } catch (Exception ex) { status.Text = ex.Message; } finally { polling.Release(); }
  }
  void OpenTip(object sender, RoutedEventArgs e) { try { ThreadHelper.ThrowIfNotOnUIThread(); if (!(tips.SelectedItem is Tip tip)) return; var dte = (DTE)Package.GetGlobalService(typeof(DTE)); var path = Path.IsPathRooted(tip.File) ? tip.File : Path.Combine(root, tip.File); var doc = dte.ItemOperations.OpenFile(path); ((TextSelection)doc.Document.Selection).GotoLine(Math.Max(1,tip.Line), true); MessageBox.Show(tip.Description, "OpenTips"); } catch (Exception ex) { status.Text = ex.Message; } }
  async void Dismiss(object sender, RoutedEventArgs e) { try { if (!(tips.SelectedItem is Tip tip)) return; await Rpc("delete_tip", new JObject { ["tip_id"] = tip.Id }); await Refresh(); } catch (Exception ex) { status.Text = ex.Message; } }
  void Stop() { timer.Stop(); watcher?.Dispose(); watcher = null; if (process != null) { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) {} process.Dispose(); process = null; } port = 0; }
 }
}



