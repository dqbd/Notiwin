﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Windows;

namespace Notiwin {
    public partial class App : Application {
        private LoginWindow loginWindow;

        private System.Windows.Forms.NotifyIcon icon;
        private System.Windows.Forms.ContextMenu contextMenu;

        private NotificationUtils notification;
        private WebsocketUtils websocket;
        private List<JObject> pushHistory;

        private void Application_Startup(object sender, StartupEventArgs e) {
            RegistryUtils.RegisterApp();

            websocket = new WebsocketUtils();
            notification = new NotificationUtils();
            pushHistory = new List<JObject>();

            contextMenu = new System.Windows.Forms.ContextMenu();
            icon = new System.Windows.Forms.NotifyIcon()
            {
                Visible = true,
                Text = ResourceAssembly.GetName().Name,
                Icon = Notiwin.Properties.Resources.Tray,
                ContextMenu = contextMenu
            };

            websocket.LoginError += OnLoginError;
            websocket.Data += OnData;
            RebuildContextMenu();

            NotificationActivator.Action += OnAction;
            NotificationActivator.Initialize();

            OpenLoginWindow(true);

            Init();
        }

        private void OnApiKey(string apikey) {
            Notiwin.Properties.Settings.Default.ApiKey = apikey;
            Notiwin.Properties.Settings.Default.Save();

            if (loginWindow?.WindowState == WindowState.Normal && websocket.IsConnected()) {
                CloseLoginWindow();
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e) {
            if (icon != null) {
                icon.Visible = false;
                icon.Dispose();
            }

            if (websocket != null) {
                websocket.Data -= OnData;
                websocket.Disconnect();
            }

            NotificationActivator.Uninitialize();
        }

        private void Init() {
            websocket.Token = Notiwin.Properties.Settings.Default.Token;
            websocket.Connect();
        }

        private void OnClose(object sender, EventArgs e) {
            Current.Shutdown();
        }

        private void RebuildContextMenu() {
            contextMenu.MenuItems.Clear();

            contextMenu.MenuItems.Add(new System.Windows.Forms.MenuItem("&Silent mode " + ((Notiwin.Properties.Settings.Default.Silent) ? "OFF" : "ON"), OnSilentToggle));
            contextMenu.MenuItems.Add(new System.Windows.Forms.MenuItem("&Ignore dismissal " + ((Notiwin.Properties.Settings.Default.NoDismiss) ? "OFF" : "ON"), OnDismissToggle));
            contextMenu.MenuItems.Add(new System.Windows.Forms.MenuItem("-"));
            contextMenu.MenuItems.Add(new System.Windows.Forms.MenuItem("&Exit", OnClose));
        }

        private void OnDismissToggle(object sender, EventArgs e) {
            Notiwin.Properties.Settings.Default.NoDismiss = !Notiwin.Properties.Settings.Default.NoDismiss;
            Notiwin.Properties.Settings.Default.Save();

            RebuildContextMenu();
        }

        private void OnSilentToggle(object sender, EventArgs e) {
            Notiwin.Properties.Settings.Default.Silent = !Notiwin.Properties.Settings.Default.Silent;
            Notiwin.Properties.Settings.Default.Save();

            RebuildContextMenu();
        }

        private async void OnAction(IDictionary<string, string> data) {
            JObject push = pushHistory.Find(item => ((string)item["notification_id"]).Equals(data["notification_id"]));

            switch (data["action"]) {
                case "reply":
                    string message = data["message"];
                   
                    if (data["notification_id"].Contains("sms_")) {
                        throw new NotImplementedException("Not implemented SMS send");
                    } else {
                        PushbulletUtils.QuickReply(push, message);
                    }
                    break;
                case "activate":
                    try {
                        string packageName = (string)push["package_name"];
                        System.Diagnostics.Process.Start(await PushbulletUtils.ResolveUrlFromPackageName(packageName));
                    } catch { }
                    break;

                case "dismiss":
                default:
                    break;
            }

            pushHistory?.RemoveAll(item => ((string)item["notification_id"]).Equals(data["notification_id"]));
        }

        private async void OnData(JObject payload) {
            string check = (string)payload["type"];

            if (check.Equals("push")) {

                JObject push = (JObject)payload["push"];
                pushHistory.Add(push);

                string type = (string)push["type"];
                string id = (string)push["notification_id"];

                if (type.Equals("mirror")) {

                    NotificationUtils.ToastFactory factory = new NotificationUtils.ToastFactory()
                    {
                        Id = id,
                        Label = (string)push["title"],
                        Body = (string)push["body"],
                        Image = PushbulletUtils.ConvertBase64Image((string)push["icon"]),
                        AppName = (string)push["application_name"]
                    };

                    if (push["actions"] != null) {
                        factory.AddActions((JArray)push["actions"]);
                    }

                    if (push["conversation_iden"] != null) {
                        factory.AddReply();
                    }

                    notification.ShowNotification(factory);

                } else if (type.Equals("dismissal")) {

                    pushHistory?.RemoveAll(item => ((string)push["notification_id"]).Equals(id));
                    notification.DismissNotification(id);
                } else if (type.Equals("sms_changed")) {
                    // TODO: check if SMS is working
                    if (push["notifications"] != null) {
                        foreach (JObject sms in ((JArray)push["notifications"])) {
                            
                            NotificationUtils.ToastFactory factory = new NotificationUtils.ToastFactory()
                            {
                                Id = "sms_" + push["source_device_iden"] + "|" + sms["thread_id"] + "|" + sms["timestamp"],
                                Label = (string)sms["title"],
                                Body = (string)sms["body"],
                                Image = await PushbulletUtils.DownloadImage((string)sms["image_url"]),
                            };

                            factory.AddReply();

                            notification.ShowNotification(factory);
                        }
                    }
                }
            }
        }

        private void OnLoginError() {
            Current.Dispatcher.Invoke(delegate {
                OpenLoginWindow();
            });
        }

        private void OpenLoginWindow(bool openHidden = false) {
            if (loginWindow == null) {
                loginWindow = new LoginWindow();
                loginWindow.TokenAccept += OnTokenAccept;
                loginWindow.TokenDeny += OnTokenDeny;
                loginWindow.ApiKey += OnApiKey;
            }

            if (!openHidden) {
                loginWindow.Show();
            }
        }

        private void CloseLoginWindow() {
            if (loginWindow != null) {
                loginWindow.Close();
                loginWindow.TokenAccept -= OnTokenAccept;
                loginWindow.TokenDeny -= OnTokenDeny;
                loginWindow.ApiKey -= OnApiKey;
            }

            loginWindow = null;
        }

        private void OnTokenDeny() {
            CloseLoginWindow();
            Current.Shutdown();
        }

        private void OnTokenAccept(string token) {
            Notiwin.Properties.Settings.Default.Token = token;
            Notiwin.Properties.Settings.Default.Save();

            CloseLoginWindow();
            Init();
        }
    }
}
