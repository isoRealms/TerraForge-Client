using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Network;
using ClassicUO.Resources;
using ClassicUO.Renderer;
using ClassicUO.Utility;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using static ClassicUO.Game.UI.Gumps.WorldMapGump;

namespace ClassicUO.Game.UI.Gumps
{
    internal sealed class MarkersManagerGump : Gump
    {
        private enum ButtonsOption
        {
            SEARCH_BTN = 100,
            CLEAR_SEARCH_BTN,
            IMPORT_SOS,
            IMPORT_TMAP,
        }

        private const int WIDTH = 620;
        private const int HEIGHT = 500;

        private const ushort HUE_FONT = 0xFFFF;

        private static readonly Dictionary<string, WMapMarkerFile> _markerFiles = MarkerFiles;

        private bool _isMarkerListModified;

        private readonly ScrollArea _scrollArea;

        private readonly SearchTextBoxControl _searchTextBox;
        private readonly NiceButton _importSOSButton, _importTMapButton;

        private string _searchText = string.Empty;
        private string _categoryId = string.Empty;

        private WMapMarkerFile _file;

        private readonly int MARKERS_CATEGORY_GROUP_INDEX = 10;

        internal MarkersManagerGump() : base(0, 0)
        {
            X = 50;
            Y = 50;
            CanMove = true;
            AcceptMouseInput = true;

            var button_width = 50;

            if (_markerFiles.Count > 0)
            {
                _file = _markerFiles.Values.First();
                button_width = WIDTH / _markerFiles.Count;
            }

            Add(new AlphaBlendControl(0.95f)
            {
                X = 1,
                Y = 1,
                Width = WIDTH,
                Height = HEIGHT,
                Hue = 999,
                AcceptMouseInput = true,
                CanCloseWithRightClick = true,
                CanMove = true,
            });

            #region Border

            Add(new Line(0, 0, WIDTH, 1, Color.Gray.PackedValue));
            Add(new Line(0, 0, 1, HEIGHT, Color.Gray.PackedValue));
            Add(new Line(0, HEIGHT, WIDTH, 1, Color.Gray.PackedValue));
            Add(new Line(WIDTH, 0, 1, HEIGHT, Color.Gray.PackedValue));

            #endregion

            var initX = 10;
            var initY = 10;

            // Search Field
            Add(_searchTextBox = new SearchTextBoxControl(10, initY));

            initX += _searchTextBox.Width + 10;

            // Import SOS
            Add(_importSOSButton = new NiceButton(initX, initY, 80, 25, ButtonAction.Activate, ResGumps.SOSMarkerImport)
            {
                ButtonParameter = (int)ButtonsOption.IMPORT_SOS,
                IsSelectable = false,
                TextLabel =
                {
                    Hue = 0x33
                }
            });

            initX += _importSOSButton.Width + 10;

            // Import Treasure Map
            Add(_importTMapButton = new NiceButton(initX, initY, 80, 25, ButtonAction.Activate, ResGumps.TMapMarkerImport)
            {
                ButtonParameter = (int)ButtonsOption.IMPORT_TMAP,
                IsSelectable = false,
                TextLabel =
                {
                    Hue = 0x33
                }
            });

            Add(new Line(0, initY + 30, WIDTH, 1, Color.Gray.PackedValue));

            initY += 40;

            #region Legend

            Add(new Label(ResGumps.MarkerIcon, true, HUE_FONT, 185, 255, FontStyle.BlackBorder) { X = 5, Y = initY });
            Add(new Label(ResGumps.MarkerName, true, HUE_FONT, 185, 255, FontStyle.BlackBorder) { X = 50, Y = initY });
            Add(new Label(ResGumps.MarkerX, true, HUE_FONT, 35, 255, FontStyle.BlackBorder) { X = 315, Y = initY });
            Add(new Label(ResGumps.MarkerY, true, HUE_FONT, 35, 255, FontStyle.BlackBorder) { X = 380, Y = initY });
            Add(new Label(ResGumps.MarkerColor, true, HUE_FONT, 35, 255, FontStyle.BlackBorder) { X = 420, Y = initY });
            Add(new Label(ResGumps.Edit, true, HUE_FONT, 35, 255, FontStyle.BlackBorder) { X = 475, Y = initY });
            Add(new Label(ResGumps.Remove, true, HUE_FONT, 40, 255, FontStyle.BlackBorder) { X = 505, Y = initY });
            Add(new Label(ResGumps.MarkerGoTo, true, HUE_FONT, 40, 255, FontStyle.BlackBorder) { X = 550, Y = initY });

            #endregion

            Add(new Line(0, initY + 20, WIDTH, 1, Color.Gray.PackedValue));

            Add(_scrollArea = new ScrollArea(10, 80, WIDTH - 20, 370, true));

            DrawArea(_markerFiles[_categoryId].IsEditable);

            initX = 0;

            foreach (var file in _markerFiles.Values)
            {
                var b = new NiceButton(button_width * initX, HEIGHT - 40, button_width, 40, ButtonAction.Activate, file.Name, MARKERS_CATEGORY_GROUP_INDEX)
                {
                    ButtonParameter = initX,
                    IsSelectable = true,
                };

                b.SetTooltip(file.Name);

                if (initX == 0)
                {
                    b.IsSelected = true;
                }

                Add(b);

                Add(new Line(b.X, b.Y, 1, b.Height, Color.Gray.PackedValue));

                initX++;
            }

            Add(new Line(0, HEIGHT - 40, WIDTH, 1, Color.Gray.PackedValue));

            SetInScreen();
        }

        private void DrawArea(bool isEditable)
        {
            _scrollArea.Clear();

            var idx = 0;

            foreach (var marker in _file.Markers)
            {
                if (!string.IsNullOrWhiteSpace(_searchText) && marker.Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var newElement = new MarkerManagerControl(marker, idx * 25, isEditable);

                newElement.OnRemoveMarker += OnRemoveMarker;
                newElement.OnEditMarker += OnEditMarker;

                _scrollArea.Add(newElement);

                ++idx;
            }
        }

        private void OnRemoveMarker(object sender, WMapMarker marker)
        {
            if (_file.Markers.Remove(marker))
            {
                //Redraw List
                DrawArea(_file.IsEditable);

                //Mark list as Modified
                _isMarkerListModified = true;
            }
        }

        private void OnEditMarker(object sender, WMapMarker marker)
        {
            _isMarkerListModified = true;
        }

        public override void OnButtonClick(int buttonID)
        {
            switch (buttonID)
            {
                case (int)ButtonsOption.SEARCH_BTN:
                {
                    if (_searchText.Equals(_searchTextBox.SearchText))
                    {
                        return;
                    }

                    _searchText = _searchTextBox.SearchText;
                    break;
                }

                case (int)ButtonsOption.CLEAR_SEARCH_BTN:
                {
                    _searchTextBox.ClearText();
                    _searchText = "";
                    break;
                }

                case (int)ButtonsOption.IMPORT_SOS:
                {
                    BeginTargetSOS();
                    break;
                }

                case (int)ButtonsOption.IMPORT_TMAP:
                {
                    BeginTargetTMap();
                    break;
                }

                default:
                {
                    _categoryId = _markerFiles.Keys.ElementAt(buttonID);
                    _file = _markerFiles[_categoryId];
                    break;
                }
            }

            DrawArea(_markerFiles[_categoryId].IsEditable);
        }

        public override void OnKeyboardReturn(int textID, string text)
        {
            if (_searchText.Equals(_searchTextBox.SearchText))
            {
                return;
            }

            _searchText = _searchTextBox.SearchText;

            DrawArea(_markerFiles[_categoryId].IsEditable);
        }

        #region SOS + TMap Markers

        static MarkersManagerGump()
        {
            UIManager.OnAdded += HandleSOSGump;
            UIManager.OnAdded += HandleTMapGump;

            PacketHandlers.OnItemUpdated += HandleTMapUpdate;
        }

        #region SOS

        private static Item _sosItem;
        private static int _sosAttempts;

        private static void SendSOSFailedMessage()
        {
            GameActions.Print(ResGumps.SOSMarkerFailed);
        }

        private static void SendSOSUpdatedMessage()
        {
            GameActions.Print(ResGumps.SOSMarkerUpdated);
        }

        private static void SendSOSAddedMessage()
        {
            GameActions.Print(ResGumps.SOSMarkerAdded);
        }

        public static void BeginTargetSOS()
        {
            _sosItem = null;
            _sosAttempts = 0;

            GameActions.Print(ResGumps.SOSMarkerTarget);

            TargetManager.SetLocalTargeting(TargetType.Neutral, OnTargetSOS);
        }

        private static void OnTargetSOS(LastTargetInfo target)
        {
            if (target.IsEntity)
            {
                var obj = World.Get(target.Serial) as Item;

                if (obj?.Name?.EndsWith("SOS") == true)
                {
                    _sosItem = obj;

                    NetClient.Socket.Send_DoubleClick(obj.Serial);

                    return;
                }
            }

            SendSOSFailedMessage();
        }

        private static void HandleSOSGump(object sender, Gump gump)
        {
            if (_sosItem == null)
            {
                return;
            }

            var userFile = UserMarkersFile;

            if (userFile == null)
            {
                _sosItem = null;
                _sosAttempts = 0;

                SendSOSFailedMessage();

                return;
            }

            if (!gump.IsFromServer)
            {
                return;
            }

            Match match = null;

            foreach (var c in gump.Children)
            {
                if (c is HtmlControl h)
                {
                    match = Regex.Match(h.Text, @"\d+[o|°]\s?\d+'[N|S],\s+\d+[o|°]\s?\d+'[E|W]");

                    if (match.Success)
                    {
                        break;
                    }
                }
            }

            if (match?.Success != true)
            {
                if (++_sosAttempts >= 10)
                {
                    _sosItem = null;
                    _sosAttempts = 0;

                    SendSOSFailedMessage();
                }

                return;
            }

            try
            {
                var markerX = -1;
                var markerY = -1;

                ConvertCoords(match.Value, ref markerX, ref markerY);

                var cmp = StringComparison.OrdinalIgnoreCase;

                if (userFile.Markers.Exists(m => m.IconName.IndexOf("SOS", cmp) >= 0 && m.MapId < 0 && m.X == markerX && m.Y == markerY))
                {
                    SendSOSUpdatedMessage();
                    return;
                }

                WMapMarker marker = new()
                {
                    X = markerX,
                    Y = markerY,
                    MapId = -1,
                    Name = _sosItem.Name,
                    IconName = "SOS",
                    ColorName = "green",
                    ZoomIndex = 3
                };

                userFile.Markers.Add(marker);

                SendSOSAddedMessage();

                var manager = UIManager.GetGump<MarkersManagerGump>();

                if (manager?._file == userFile)
                {
                    manager._isMarkerListModified = true;

                    manager.DrawArea(userFile.IsEditable);
                }
                else
                {
                    File.WriteAllLines(userFile.FullPath, userFile.Markers.Select(m => $"{m.X},{m.Y},{m.MapId},{m.Name},{m.IconName},{m.ColorName},{m.ZoomIndex}"));
                }

                var wmGump = UIManager.GetGump<WorldMapGump>();

                if (wmGump?.MapIndex is 0 or 1)
                {
                    wmGump.GoToMarker(marker.X, marker.Y, false);
                }
            }
            finally
            {
                gump.InvokeMouseCloseGumpWithRClick();

                BeginTargetSOS();
            }
        }

        #endregion

        #region TMap

        private static Item _tmapItem;

        private static void HandleTMapUpdate(object sender, Item item)
        {
            if (item?.OnGround != true)
            {
                return;
            }

            if (item.Name?.IndexOf("treasure chest", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            var userFile = UserMarkersFile;

            if (userFile == null)
            {
                _tmapItem = null;

                SendTMapFailedMessage();

                return;
            }

            var cmp = StringComparison.OrdinalIgnoreCase;

            if (userFile.Markers.RemoveAll(m => m.IconName.IndexOf("TMAP", cmp) >= 0 && m.MapId == World.MapIndex && m.X == item.X && m.Y == item.Y) > 0)
            {
                SendTMapUpdatedMessage();

                var manager = UIManager.GetGump<MarkersManagerGump>();

                if (manager?._file == userFile)
                {
                    manager._isMarkerListModified = true;

                    manager.DrawArea(userFile.IsEditable);
                }
                else
                {
                    File.WriteAllLines(userFile.FullPath, userFile.Markers.Select(m => $"{m.X},{m.Y},{m.MapId},{m.Name},{m.IconName},{m.ColorName},{m.ZoomIndex}"));
                }
            }
        }

        private static void SendTMapFailedMessage()
        {
            GameActions.Print(ResGumps.TMapMarkerFailed);
        }

        private static void SendTMapUpdatedMessage()
        {
            GameActions.Print(ResGumps.TMapMarkerUpdated);
        }

        private static void SendTMapAddedMessage()
        {
            GameActions.Print(ResGumps.TMapMarkerAdded);
        }

        public static void BeginTargetTMap()
        {
            _tmapItem = null;

            GameActions.Print(ResGumps.TMapMarkerTarget);

            TargetManager.SetLocalTargeting(TargetType.Neutral, OnTargetTMap);
        }

        private static void OnTargetTMap(LastTargetInfo target)
        {
            if (target.IsEntity)
            {
                var obj = World.Get(target.Serial) as Item;

                var cmp = StringComparison.OrdinalIgnoreCase;

                if (obj?.Name?.IndexOf("treasure map", cmp) >= 0 && obj.Name.IndexOf("tattered", cmp) < 0)
                {
                    _tmapItem = obj;

                    NetClient.Socket.Send_DoubleClick(obj.Serial);

                    return;
                }
            }

            SendTMapFailedMessage();
        }

        private static void HandleTMapGump(object sender, Gump gump)
        {
            if (_tmapItem == null)
            {
                return;
            }

            var userFile = UserMarkersFile;

            if (userFile == null)
            {
                _tmapItem = null;

                SendTMapFailedMessage();

                return;
            }

            if (gump is not MapGump mapGump)
            {
                return;
            }

            Point? pin = null;

            foreach (var c in mapGump.Children)
            {
                if (c is MapGump.PinControl p)
                {
                    pin = new(p.InitX, p.InitY);
                    break;
                }
            }

            if (pin == null)
            {
                static void redirect(int mx, int my, MapGump.PinControl mp)
                {
                    HandleTMapGump(mp, mp.Parent as Gump);
                }

                mapGump.OnPinAdded -= redirect;
                mapGump.OnPinAdded += redirect;

                return;
            }

            try
            {
                var markerX = pin.Value.X;
                var markerY = pin.Value.Y;

                var cmp = StringComparison.OrdinalIgnoreCase;

                if (userFile.Markers.Exists(m => m.IconName.IndexOf("TMAP", cmp) >= 0 && m.MapId == mapGump.MapId && m.X == markerX && m.Y == markerY))
                {
                    SendTMapUpdatedMessage();
                    return;
                }

                WMapMarker marker = new()
                {
                    X = markerX,
                    Y = markerY,
                    MapId = mapGump.MapId,
                    Name = _tmapItem.Name,
                    IconName = "TMAP",
                    ColorName = "yellow",
                    ZoomIndex = 3
                };

                userFile.Markers.Add(marker);

                SendTMapAddedMessage();

                var manager = UIManager.GetGump<MarkersManagerGump>();

                if (manager?._file == userFile)
                {
                    manager._isMarkerListModified = true;

                    manager.DrawArea(userFile.IsEditable);
                }
                else
                {
                    File.WriteAllLines(userFile.FullPath, userFile.Markers.Select(m => $"{m.X},{m.Y},{m.MapId},{m.Name},{m.IconName},{m.ColorName},{m.ZoomIndex}"));
                }

                var wmGump = UIManager.GetGump<WorldMapGump>();

                if (wmGump?.MapIndex == mapGump.MapId)
                {
                    wmGump.GoToMarker(marker.X, marker.Y, false);
                }
            }
            finally
            {
                gump.InvokeMouseCloseGumpWithRClick();

                BeginTargetTMap();
            }
        }

        #endregion

        #endregion

        public override void Dispose()
        {
            if (_isMarkerListModified)
            {
                File.WriteAllLines(UserMarkersFilePath, _file.Markers.Select(m => $"{m.X},{m.Y},{m.MapId},{m.Name},{m.IconName},{m.ColorName},{m.ZoomIndex}"));

                _isMarkerListModified = false;
            }

            base.Dispose();
        }

        internal class DrawTexture : Control
        {
            public Texture2D Texture;

            public DrawTexture(Texture2D texture)
            {
                Texture = texture;
                Width = Height = 15;
            }

            public override bool Draw(UltimaBatcher2D batcher, int x, int y)
            {
                if (Texture == null)
                {
                    return false;
                }

                var hueVector = ShaderHueTranslator.GetHueVector(0);

                batcher.Draw(Texture, new Rectangle(x, y + 7, Width, Height), hueVector);

                return true;
            }
        }

        private sealed class MarkerManagerControl : Control
        {
            private enum ButtonsOption
            {
                EDIT_MARKER_BTN,
                REMOVE_MARKER_BTN,
                GOTO_MARKER_BTN
            }

            private readonly WMapMarker _marker;

            private readonly Label _labelName, _labelX, _labelY, _labelColor;

            private readonly DrawTexture _iconTexture;

            public event EventHandler<WMapMarker> OnRemoveMarker, OnEditMarker;

            public MarkerManagerControl(WMapMarker marker, int y, bool isEditable)
            {
                CanMove = true;

                _marker = marker;

                Add(_iconTexture = new DrawTexture(_marker.Icon)
                {
                    X = 0,
                    Y = y - 5
                });

                Add(_labelName = new Label($"{_marker.Name}", true, HUE_FONT, 280)
                {
                    X = 30,
                    Y = y
                });

                Add(_labelX = new Label($"{_marker.X}", true, HUE_FONT, 35)
                {
                    X = 305,
                    Y = y
                });

                Add(_labelY = new Label($"{_marker.Y}", true, HUE_FONT, 35)
                {
                    X = 350,
                    Y = y
                });

                Add(_labelColor = new Label($"{_marker.ColorName}", true, HUE_FONT, 35)
                {
                    X = 410,
                    Y = y
                });

                if (isEditable)
                {
                    Add(new Button((int)ButtonsOption.EDIT_MARKER_BTN, 0xFAB, 0xFAC)
                    {
                        X = 470,
                        Y = y,
                        ButtonAction = ButtonAction.Activate,
                    });

                    Add(new Button((int)ButtonsOption.REMOVE_MARKER_BTN, 0xFB1, 0xFB2)
                    {
                        X = 505,
                        Y = y,
                        ButtonAction = ButtonAction.Activate,
                    });
                }

                Add(new Button((int)ButtonsOption.GOTO_MARKER_BTN, 0xFA5, 0xFA7)
                {
                    X = 540,
                    Y = y,
                    ButtonAction = ButtonAction.Activate,
                });
            }

            public override void OnButtonClick(int buttonId)
            {
                switch (buttonId)
                {
                    case (int)ButtonsOption.EDIT_MARKER_BTN:
                    {
                        var editGump = UIManager.GetGump<UserMarkerGump>();

                        editGump?.Dispose();

                        editGump = new UserMarkerGump(_marker);

                        editGump.OnMarkerEdit += OnEditEnd;

                        UIManager.Add(editGump);

                        break;
                    }

                    case (int)ButtonsOption.REMOVE_MARKER_BTN:
                    {
                        OnRemoveMarker.Raise(_marker, this);

                        break;
                    }

                    case (int)ButtonsOption.GOTO_MARKER_BTN:
                    {
                        var wmGump = UIManager.GetGump<WorldMapGump>();

                        wmGump?.GoToMarker(_marker.X, _marker.Y, false);

                        break;
                    }
                }
            }

            private void OnEditEnd(object sender, WMapMarker marker)
            {
                _labelName.Text = marker.Name;
                _labelColor.Text = marker.ColorName;
                _labelX.Text = $"{marker.X}";
                _labelY.Text = $"{marker.Y}";

                _iconTexture.Texture = marker.Icon;

                OnEditMarker.Raise(marker, this);
            }
        }

        private sealed class SearchTextBoxControl : Control
        {
            private readonly StbTextBox _textBox;

            public string SearchText => _textBox.Text;

            public SearchTextBoxControl(int x, int y)
            {
                AcceptMouseInput = true;
                AcceptKeyboardInput = true;

                Add(new Label(ResGumps.MarkerSearch, true, HUE_FONT, 50, 1)
                {
                    X = x,
                    Y = y + 5
                });

                Add(new ResizePic(0x0BB8)
                {
                    X = x + 50,
                    Y = y,
                    Width = 200,
                    Height = 25
                });

                Add(_textBox = new StbTextBox(0xFF, 30, 200, true, FontStyle.BlackBorder | FontStyle.Fixed)
                {
                    X = x + 53,
                    Y = y + 3,
                    Width = 200,
                    Height = 25
                });

                Add(new Button((int)ButtonsOption.SEARCH_BTN, 0xFB7, 0xFB9)
                {
                    X = x + 250,
                    Y = y + 1,
                    ButtonAction = ButtonAction.Activate,
                });

                Add(new Button((int)ButtonsOption.CLEAR_SEARCH_BTN, 0xFB1, 0xFB2)
                {
                    X = x + 285,
                    Y = y + 1,
                    ButtonAction = ButtonAction.Activate,
                });

                Width = Children.Max(c => c.X + c.Width);
                Height = Children.Max(c => c.Y + c.Height);
            }

            public void ClearText()
            {
                _textBox.SetText("");
            }
        }
    }
}