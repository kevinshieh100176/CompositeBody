using System.Linq;

namespace Unity.Services.Multiplayer.Editor
{
    class SessionsViewer
    {
        internal ISession Session;
        internal UIController UI;

        UIElement SessionContainer;

        public SessionsViewer(UIController ui, ISession session)
        {
            UI = ui;
            Session = session;
        }

        public void CreateGUI()
        {
            SessionContainer = UI.Element().SetMargin(5, 0, 5, 0);

            RefreshSessionGUI();

            UI.Button("Refresh", RefreshSessionGUI)
                .SetWidth(150);
        }

        /// <summary>Refreshes the session list in the editor. Call when session data changes to update the display.</summary>
        internal void RefreshSessionGUI()
        {
            if (SessionContainer == null || Session == null)
                return;

            SessionContainer.Clear();

            using (SessionContainer.Scope())
            {
                CreateSessionGUI();
            }
        }

        void CreateSessionGUI()
        {
            if (!string.IsNullOrEmpty(Session.Type))
            {
                UI.H4(Session.Type.ToString());
            }

            CreateSessionInfoGUI();
            CreatePropertiesGUI();
            CreatePlayersGUI();
            CreateNetworkGUI();
        }

        void CreateSessionInfoGUI()
        {
            UI.Label("Session Info").SetFontStyleBold().SetMarginTop(5);
            UI.Separator();

            using (UI.HorizontalElement().Scope())
            {
                UI.Label("State").SetWidth(100);
                UI.SelectableLabel().SetText(Session.State.ToString());
            }
            using (UI.HorizontalElement().Scope())
            {
                UI.Label("Id").SetWidth(100);
                UI.SelectableLabel().SetText(Session.Id);
            }

            using (UI.HorizontalElement().Scope())
            {
                UI.Label("Name").SetWidth(100);
                UI.SelectableLabel().SetText(Session.Name);
            }

            using (UI.HorizontalElement().Scope())
            {
                UI.Label("MaxPlayers").SetWidth(100);
                UI.SelectableLabel().SetText(Session.MaxPlayers.ToString());
            }

            using (UI.HorizontalElement().Scope())
            {
                UI.Label("IsPrivate").SetWidth(100);
                UI.SelectableLabel().SetText(Session.IsPrivate.ToString());
            }

            using (UI.HorizontalElement().Scope())
            {
                UI.Label("IsLocked").SetWidth(100);
                UI.SelectableLabel().SetText(Session.IsLocked.ToString());
            }

            using (UI.HorizontalElement().Scope())
            {
                UI.Label("Code").SetWidth(100);
                UI.SelectableLabel().SetText(Session.Code);
            }

            using (UI.HorizontalElement().Scope())
            {
                UI.Label("Host").SetWidth(100);
                UI.SelectableLabel().SetText(Session.Host);
            }

            using (UI.HorizontalElement().Scope())
            {
                UI.Label("Is Host").SetWidth(100);
                UI.SelectableLabel().SetText(Session.IsHost.ToString());
            }
        }

        void CreatePropertiesGUI()
        {
            UI.Label("Session Properties").SetFontStyleBold().SetMarginTop(5);
            UI.Separator();

            if (Session.Properties.Count == 0)
            {
                using (UI.HorizontalLayout().Scope())
                {
                    UI.Label("None.");
                }
            }
            else
            {
                for (var i = 0; i != Session.Properties.Count; ++i)
                {
                    var property = Session.Properties.ElementAt(i);

                    using (UI.HorizontalLayout().Scope())
                    {
                        UI.Label().SetText(property.Key).SetWidth(150);
                        UI.Label().SetText(property.Value.Visibility.ToString()).SetWidth(100);
                        UI.Label().SetText(property.Value.Value).Wrap().SetFlexShrink(1);
                    }
                }
            }
        }

        void CreateNetworkGUI()
        {
            var network = Session.NetworkModule;

            using (UI.VerticalElement().Scope())
            {
                UI.Label("Network").SetFontStyleBold().SetMarginTop(5);
                UI.Separator();

                using (UI.HorizontalElement().Scope())
                {
                    UI.Label("State").SetWidth(100);
                    UI.SelectableLabel().SetText(network.State.ToString());
                }

                if (network.NetworkInfo != null && network.NetworkMetadata != null)
                {
                    using (UI.HorizontalElement().Scope())
                    {
                        UI.Label("Type").SetWidth(100);
                        UI.SelectableLabel().SetText(network.NetworkMetadata.Network.ToString());
                    }

                    using (UI.VerticalElement().Scope())
                    {
                        switch (network.NetworkMetadata.Network)
                        {
                            case NetworkType.Relay:
                                using (UI.HorizontalElement().Scope())
                                {
                                    UI.Label("Protocol").SetWidth(100);
                                    var protocol = network.NetworkOptions?.RelayProtocol ??
                                        RelayProtocol.Default;
                                    UI.SelectableLabel().SetText(protocol.ToString());
                                }

                                using (UI.HorizontalElement().Scope())
                                {
                                    UI.Label("Allocation Id").SetWidth(100);
                                    UI.SelectableLabel().SetText(network.RelayHandler.AllocationId.ToString());
                                }

                                using (UI.HorizontalElement().Scope())
                                {
                                    UI.Label("Region").SetWidth(100);
                                    UI.SelectableLabel().SetText(network.RelayHandler.Region);
                                }

                                using (UI.HorizontalElement().Scope())
                                {
                                    UI.Label("Join Code").SetWidth(100);
                                    UI.SelectableLabel().SetText(network.RelayHandler.RelayJoinCode);
                                }
                                break;
                            case NetworkType.DistributedAuthority:
                                using (UI.HorizontalElement().Scope())
                                {
                                    UI.Label("Protocol").SetWidth(100);
                                    var protocol = network.NetworkOptions?.RelayProtocol ??
                                        RelayProtocol.Default;
                                    UI.SelectableLabel().SetText(protocol.ToString());
                                }

                                using (UI.HorizontalElement().Scope())
                                {
                                    UI.Label("Allocation Id").SetWidth(100);
                                    UI.SelectableLabel().SetText(network.DaHandler.AllocationId.ToString());
                                }

                                using (UI.HorizontalElement().Scope())
                                {
                                    UI.Label("Region").SetWidth(100);
                                    UI.SelectableLabel().SetText(network.DaHandler.Region);
                                }

                                using (UI.HorizontalElement().Scope())
                                {
                                    UI.Label("Join Code").SetWidth(100);
                                    UI.SelectableLabel().SetText(network.DaHandler.RelayJoinCode);
                                }
                                break;
                            case NetworkType.Direct:
                                using (UI.HorizontalElement().Scope())
                                {
                                    UI.Label("Endpoint").SetWidth(100);
                                    UI.SelectableLabel().SetText(network.NetworkMetadata.Endpoint.Address);
                                }
                                break;
                        }
                    }
                }
            }
        }

        void CreatePlayersGUI()
        {
            UI.Label("Players").SetFontStyleBold().SetMarginTop(5);
            UI.Separator();

            for (var i = 0; i != Session.Players.Count; ++i)
            {
                CreatePlayerGUI(Session.Players[i]);
            }
        }

        void CreatePlayerGUI(IReadOnlyPlayer player)
        {
            using (UI.HeaderPanel().Scope())
            {
                if (!string.IsNullOrEmpty(player.GetPlayerName()))
                {
                    UI.SelectableLabel().SetMargin(0).SetPadding(0).SetFontStyleBold().SetText(player.GetPlayerName());
                }
                else
                {
                    UI.SelectableLabel().SetMargin(0).SetPadding(0).SetFontStyleBold().SetText(player.Id.ToString());
                }

                if (player == Session.CurrentPlayer)
                {
                    UI.Label("*");
                }
            }

            using (UI.ContentPanel(true).SetMarginBottom(5).Scope())
            {
                if (!string.IsNullOrEmpty(player.GetPlayerName()))
                {
                    using (UI.HorizontalLayout().Scope())
                    {
                        UI.Label("Player Id:").SetWidth(100);
                        UI.SelectableLabel().SetText(player.Id).SetWidth(250);
                    }
                }

                using (UI.HorizontalLayout().Scope())
                {
                    UI.Label("Joined:").SetWidth(100);
                    UI.SelectableLabel().SetText(player.Joined.ToLocalTime().ToShortTimeString()).SetWidth(100);
                }

                using (UI.HorizontalLayout().Scope())
                {
                    UI.Label("Last Updated:").SetWidth(100);
                    UI.SelectableLabel().SetText(player.LastUpdated.ToLocalTime().ToShortTimeString()).SetWidth(100);
                }

                UI.Label("Player Properties").SetMarginTop(5).SetFontStyleBold();

                if (player.Properties.Count == 0)
                {
                    using (UI.HorizontalLayout().Scope())
                    {
                        UI.Label("None.");
                    }
                }
                else
                {
                    foreach (var property in player.Properties)
                    {
                        using (UI.HorizontalLayout().Scope())
                        {
                            UI.Label(property.Key).SetWidth(150);
                            UI.Label().SetText(property.Value.Visibility.ToString()).SetWidth(100);
                            UI.Label().SetText(property.Value.Value).Wrap().SetFlexShrink(1);
                        }
                    }
                }
            }
        }
    }
}
