using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AugurACT.Plugin
{
    internal sealed class TimelineView : UserControl
    {
        private const string VirtualHost = "augur.act";

        private readonly WebView2 webView = new WebView2 { Dock = DockStyle.Fill };
        private readonly List<string> pendingMessages = new List<string>();
        private readonly string pluginDir;
        private readonly Action<string> setStatus;
        private bool pageReady;

        public TimelineView(string pluginDir, Action<string> setStatus)
        {
            this.pluginDir = pluginDir;
            this.setStatus = setStatus;
            Controls.Add(webView);
            InitializeWebView();
        }

        public void PostMessage(string json)
        {
            if (IsDisposed)
            {
                return;
            }

            if (pageReady)
            {
                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            else
            {
                pendingMessages.Add(json);
            }
        }

        private async void InitializeWebView()
        {
            try
            {
                SetLoaderFolder();

                // ACT's install folder may not be writable, so browser state lives per-user.
                var userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AugurACT",
                    "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, userData);
                await webView.EnsureCoreWebView2Async(environment);

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHost,
                    Path.Combine(pluginDir, "web"),
                    CoreWebView2HostResourceAccessKind.DenyCors);
                webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                webView.CoreWebView2.Navigate($"https://{VirtualHost}/index.html");
            }
            catch (Exception ex)
            {
                setStatus("WebView2 failed to start: " + ex.Message);
            }
        }

        private void SetLoaderFolder()
        {
            var architecture = Environment.Is64BitProcess ? "win-x64" : "win-x86";
            try
            {
                CoreWebView2Environment.SetLoaderDllFolderPath(
                    Path.Combine(pluginDir, "runtimes", architecture, "native"));
            }
            catch (InvalidOperationException)
            {
                // The loader stays in the process when ACT disables and re-enables the plugin.
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                setStatus("Timeline page failed to load: " + e.WebErrorStatus);
                return;
            }

            pageReady = true;
            foreach (var json in pendingMessages)
            {
                webView.CoreWebView2.PostWebMessageAsJson(json);
            }

            pendingMessages.Clear();
        }
    }
}
