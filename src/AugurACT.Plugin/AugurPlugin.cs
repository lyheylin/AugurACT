using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using AugurACT.Ui;

namespace AugurACT.Plugin
{
    public sealed class AugurPlugin : IActPluginV1
    {
        private Label? status;
        private TimelineView? view;
        private EncounterBridge? bridge;
        private string pluginDir = "";

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            status = pluginStatusText;
            pluginDir = ActGlobals.oFormActMain.PluginGetSelfData(this).pluginFile.DirectoryName;

            // ACT loads plugins from raw bytes, so .NET can't probe the plugin's folder for dependencies.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromPluginDir;
            Start(pluginScreenSpace);
        }

        // Kept out of InitPlugin so JIT-compiling that method doesn't load dependencies before the handler exists.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Start(TabPage pluginScreenSpace)
        {
            view = new TimelineView(pluginDir, SetStatus) { Dock = DockStyle.Fill };
            var target = view;
            bridge = new EncounterBridge(json => PostToPage(target, json), SetStatus, EncounterStorePath());
            view.MessageReceived += bridge.OnPageMessage;
            pluginScreenSpace.Text = "AugurACT";
            pluginScreenSpace.Controls.Add(view);

            ActGlobals.oFormActMain.OnCombatEnd += OnCombatEnd;
            SetStatus("Plugin started");
        }

        public void DeInitPlugin()
        {
            ActGlobals.oFormActMain.OnCombatEnd -= OnCombatEnd;
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveFromPluginDir;
            bridge?.Dispose();
            bridge = null;
            view?.Dispose();
            view = null;
            SetStatus("Plugin exited");
        }

        private void OnCombatEnd(bool isImport, CombatToggleEventArgs encounterInfo)
        {
            bridge?.OnCombatEnd(encounterInfo.encounter);
        }

        // The page may only be touched on the UI thread.
        private static void PostToPage(TimelineView target, string json)
        {
            try
            {
                ActGlobals.oFormActMain.BeginInvoke(new Action(() => target.PostMessage(json)));
            }
            catch (InvalidOperationException)
            {
                // ACT's window is closing.
            }
        }

        // Next to ACT's other plugin settings, so it survives plugin updates and is backed up with them.
        private static string EncounterStorePath() =>
            Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Config", "AugurACT.encounters.json");

        private Assembly? ResolveFromPluginDir(object sender, ResolveEventArgs args)
        {
            var path = Path.Combine(pluginDir, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        // Called from the analysis thread too.
        private void SetStatus(string text)
        {
            var label = status;
            if (label == null || label.IsDisposed)
            {
                return;
            }

            if (label.InvokeRequired)
            {
                label.BeginInvoke(new Action(() => label.Text = text));
            }
            else
            {
                label.Text = text;
            }
        }
    }
}
