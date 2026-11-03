using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using AugurACT.Core.Encounters;
using AugurACT.Ui;

namespace AugurACT.Plugin
{
    public sealed class AugurPlugin : IActPluginV1
    {
        private Label? status;
        private TimelineView? view;
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
            pluginScreenSpace.Text = "AugurACT";
            pluginScreenSpace.Controls.Add(view);

            ActGlobals.oFormActMain.OnCombatEnd += OnCombatEnd;
            SetStatus("Plugin started");
        }

        public void DeInitPlugin()
        {
            ActGlobals.oFormActMain.OnCombatEnd -= OnCombatEnd;
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveFromPluginDir;
            view?.Dispose();
            view = null;
            SetStatus("Plugin exited");
        }

        private void OnCombatEnd(bool isImport, CombatToggleEventArgs encounterInfo)
        {
            var target = view;
            if (target == null)
            {
                return;
            }

            var encounter = encounterInfo.encounter;
            var title = encounter.Title;
            var zone = encounter.ZoneName;
            var lines = encounter.LogLines.Select(entry => entry.LogLine).ToList();
            var self = ActGlobals.charName;

            // Raid encounters can hold hundreds of thousands of lines; keep ACT's parse thread free.
            Task.Run(() =>
            {
                try
                {
                    var json = EncounterSummary.Build(title, zone, lines, self).ToJson();
                    ActGlobals.oFormActMain.BeginInvoke(new Action(() => target.PostMessage(json)));
                }
                catch (Exception ex)
                {
                    ActGlobals.oFormActMain.WriteExceptionLog(ex, "AugurACT: encounter analysis failed");
                }
            });
        }

        private Assembly? ResolveFromPluginDir(object sender, ResolveEventArgs args)
        {
            var path = Path.Combine(pluginDir, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        private void SetStatus(string text)
        {
            if (status != null)
            {
                status.Text = text;
            }
        }
    }
}
