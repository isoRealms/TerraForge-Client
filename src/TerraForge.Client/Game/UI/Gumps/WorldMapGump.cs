#region license

// Copyright (c) 2021, andreakarasho
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 1. Redistributions of source code must retain the above copyright
//    notice, this list of conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright
//    notice, this list of conditions and the following disclaimer in the
//    documentation and/or other materials provided with the distribution.
// 3. All advertising materials mentioning features or use of this software
//    must display the following acknowledgement:
//    This product includes software developed by andreakarasho - https://github.com/andreakarasho
// 4. Neither the name of the copyright holder nor the
//    names of its contributors may be used to endorse or promote products
//    derived from this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ''AS IS'' AND ANY
// EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE FOR ANY
// DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Xml;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.IO;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDL2;
using SpriteFont = ClassicUO.Renderer.SpriteFont;
using System.Text.Json.Serialization;
using static ClassicUO.Game.UI.Gumps.WorldMapGump;

namespace ClassicUO.Game.UI.Gumps
{
    [JsonSourceGenerationOptions(WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(ZonesFile), GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(ZonesFileZoneData), GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(List<ZonesFileZoneData>), GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(List<int>), GenerationMode = JsonSourceGenerationMode.Metadata)]
    internal sealed partial class ZonesJsonContext : JsonSerializerContext { }

    internal class WorldMapGump : ResizableGump
    {
        public const string USER_MARKERS_FILE = "userMarkers";

        public static readonly string UserMarkersFilePath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client", $"{USER_MARKERS_FILE}.usr");

        private static Texture2D _mapTexture;
        private static uint[] _pixelBuffer;
        private static sbyte[] _zBuffer;

        private static Point _last_position = new(100, 100);

        public static Dictionary<string, WMapMarkerFile> MarkerFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, Texture2D> MarkerIcons { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, Color> MarkerColors { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = Color.White,

            ["red"] = Color.Red,
            ["green"] = Color.Green,
            ["blue"] = Color.Blue,
            ["purple"] = Color.Purple,
            ["black"] = Color.Black,
            ["yellow"] = Color.Yellow,
            ["marine"] = Color.Aquamarine,

            ["none"] = Color.Transparent,
        };

        public static WMapMarkerFile UserMarkersFile
        {
            get
            {
                if (MarkerFiles.TryGetValue(USER_MARKERS_FILE, out var file))
                {
                    return file;
                }

                return null;
            }
        }

        #region Helpers

        public static Color GetColor(string name)
        {
            return MarkerColors.TryGetValue(name, out var color) ? color : Color.White;
        }

        public static Texture2D GetIcon(string name)
        {
            return MarkerIcons.TryGetValue(name, out var texture) ? texture : null;
        }

        public static void ConvertCoords(string coords, ref int xAxis, ref int yAxis)
        {
            var coordsSplit = coords.Split(',');

            var yCoord = coordsSplit[0];
            var xCoord = coordsSplit[1];

            // Calc Y first
            var ySplit = yCoord.Split('°', 'o');

            var yDegree = Convert.ToDouble(ySplit[0]);
            var yMinute = Convert.ToDouble(ySplit[1].Substring(0, ySplit[1].IndexOf("'", StringComparison.Ordinal)));

            if (yCoord.Substring(yCoord.Length - 1).Equals("N"))
            {
                yAxis = (int)(1624 - (yMinute / 60) * (4096.0 / 360) - yDegree * (4096.0 / 360));
            }
            else
            {
                yAxis = (int)(1624 + (yMinute / 60) * (4096.0 / 360) + yDegree * (4096.0 / 360));
            }

            // Calc X next
            var xSplit = xCoord.Split('°', 'o');

            var xDegree = Convert.ToDouble(xSplit[0]);
            var xMinute = Convert.ToDouble(xSplit[1].Substring(0, xSplit[1].IndexOf("'", StringComparison.Ordinal)));

            if (xCoord.Substring(xCoord.Length - 1).Equals("W"))
            {
                xAxis = (int)(1323 - (xMinute / 60) * (5120.0 / 360) - xDegree * (5120.0 / 360));
            }
            else
            {
                xAxis = (int)(1323 + (xMinute / 60) * (5120.0 / 360) + xDegree * (5120.0 / 360));
            }

            // Normalize values outside of map range.
            if (xAxis < 0)
            {
                xAxis += 5120;
            }
            else if (xAxis > 5120)
            {
                xAxis -= 5120;
            }

            if (yAxis < 0)
            {
                yAxis += 4096;
            }
            else if (yAxis > 4096)
            {
                yAxis -= 4096;
            }
        }

        #endregion

        private Point _center, _lastScroll, _mouseCenter, _scroll;
        private Point? _lastMousePosition = null;

        private bool _flipMap = true;
        private bool _freeView;
        private bool _isScrolling;
        private bool _isTopMost;

        private readonly string _mapFilesPath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client");
        private readonly string _mapIconsPath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client", "MapIcons");

        private int _mapIndex;

        public int MapIndex => _mapIndex;

        private bool _mapMarkersLoaded;

        private readonly HashSet<string> _hiddenMarkerFiles = [], _hiddenZoneFiles = [];

        private readonly ZoneSets _zoneSets = new();

        private SpriteFont _markerFont = Fonts.Map1;
        private int _markerFontIndex = 1;

        private readonly Dictionary<string, ContextMenuItemEntry> _options = new(StringComparer.OrdinalIgnoreCase);

        private bool _showCoordinates;
        private bool _showMouseCoordinates;
        private bool _showGroupBar = true;
        private bool _showGroupName = true;
        private bool _showMarkerIcons = true;
        private bool _showMarkerNames = true;
        private bool _showMarkers = true;
        private bool _showMobiles = true;
        private bool _showMultis = true;
        private bool _showPartyMembers = true;
        private bool _showPlayerBar = true;
        private bool _showPlayerName = true;
        private int _zoomIndex = 4;
        private bool _showGridIfZoomed = true;
        private bool _allowPositionalTarget = false;

        private WMapMarker _gotoMarker;

        private readonly float[] _zooms = [0.125f, 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 4f, 6f, 8f];

        private readonly Color _semiTransparentWhiteForGrid = new(255, 255, 255, 56);

        public WorldMapGump() : base(400, 400, 100, 100, 0, 0)
        {
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;

            X = _last_position.X;
            Y = _last_position.Y;

            LoadSettings();

            GameActions.Print(ResGumps.WorldMapLoading, 0x35);

            Load();
            OnResize();

            LoadMarkers();
            LoadZones();

            BuildGump();
        }

        public override GumpType GumpType => GumpType.WorldMap;

        public float Zoom => _zooms[_zoomIndex];

        public bool TopMost
        {
            get => _isTopMost;
            set
            {
                if (_isTopMost != value)
                {
                    _isTopMost = value;

                    SaveSettings();
                }

                ShowBorder = !_isTopMost;
                LayerOrder = _isTopMost ? UILayer.Over : UILayer.Under;
            }
        }

        public bool FreeView
        {
            get => _freeView;
            set
            {
                if (_freeView != value)
                {
                    _freeView = value;

                    SaveSettings();

                    if (!_freeView)
                    {
                        _isScrolling = false;
                        CanMove = true;
                    }
                }
            }
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);

            BuildGump();
        }

        private void LoadSettings()
        {
            Width = ProfileManager.CurrentProfile.WorldMapWidth;
            Height = ProfileManager.CurrentProfile.WorldMapHeight;

            SetFont(ProfileManager.CurrentProfile.WorldMapFont);

            ResizeWindow(new Point(Width, Height));

            _flipMap = ProfileManager.CurrentProfile.WorldMapFlipMap;
            _showPartyMembers = ProfileManager.CurrentProfile.WorldMapShowParty;

            World.WMapManager.SetEnable(_showPartyMembers);

            _zoomIndex = ProfileManager.CurrentProfile.WorldMapZoomIndex;

            _showCoordinates = ProfileManager.CurrentProfile.WorldMapShowCoordinates;
            _showMouseCoordinates = ProfileManager.CurrentProfile.WorldMapShowMouseCoordinates;
            _showMobiles = ProfileManager.CurrentProfile.WorldMapShowMobiles;

            _showPlayerName = ProfileManager.CurrentProfile.WorldMapShowPlayerName;
            _showPlayerBar = ProfileManager.CurrentProfile.WorldMapShowPlayerBar;
            _showGroupName = ProfileManager.CurrentProfile.WorldMapShowGroupName;
            _showGroupBar = ProfileManager.CurrentProfile.WorldMapShowGroupBar;
            _showMarkers = ProfileManager.CurrentProfile.WorldMapShowMarkers;
            _showMultis = ProfileManager.CurrentProfile.WorldMapShowMultis;
            _showMarkerNames = ProfileManager.CurrentProfile.WorldMapShowMarkersNames;

            _hiddenMarkerFiles.Clear();
            _hiddenMarkerFiles.UnionWith(ProfileManager.CurrentProfile.WorldMapHiddenMarkerFiles?.Split(',') ?? Enumerable.Empty<string>());

            _hiddenZoneFiles.Clear();
            _hiddenZoneFiles.UnionWith(ProfileManager.CurrentProfile.WorldMapHiddenZoneFiles?.Split(',') ?? Enumerable.Empty<string>());

            _showGridIfZoomed = ProfileManager.CurrentProfile.WorldMapShowGridIfZoomed;
            _allowPositionalTarget = ProfileManager.CurrentProfile.WorldMapAllowPositionalTarget;

            TopMost = ProfileManager.CurrentProfile.WorldMapTopMost;
            FreeView = ProfileManager.CurrentProfile.WorldMapFreeView;
        }

        public void SaveSettings()
        {
            if (ProfileManager.CurrentProfile == null)
            {
                return;
            }

            ProfileManager.CurrentProfile.WorldMapWidth = Width;
            ProfileManager.CurrentProfile.WorldMapHeight = Height;

            ProfileManager.CurrentProfile.WorldMapFlipMap = _flipMap;
            ProfileManager.CurrentProfile.WorldMapTopMost = TopMost;
            ProfileManager.CurrentProfile.WorldMapFreeView = FreeView;
            ProfileManager.CurrentProfile.WorldMapShowParty = _showPartyMembers;

            ProfileManager.CurrentProfile.WorldMapZoomIndex = _zoomIndex;

            ProfileManager.CurrentProfile.WorldMapShowCoordinates = _showCoordinates;
            ProfileManager.CurrentProfile.WorldMapShowMouseCoordinates = _showMouseCoordinates;
            ProfileManager.CurrentProfile.WorldMapShowMobiles = _showMobiles;

            ProfileManager.CurrentProfile.WorldMapShowPlayerName = _showPlayerName;
            ProfileManager.CurrentProfile.WorldMapShowPlayerBar = _showPlayerBar;
            ProfileManager.CurrentProfile.WorldMapShowGroupName = _showGroupName;
            ProfileManager.CurrentProfile.WorldMapShowGroupBar = _showGroupBar;
            ProfileManager.CurrentProfile.WorldMapShowMarkers = _showMarkers;
            ProfileManager.CurrentProfile.WorldMapShowMultis = _showMultis;
            ProfileManager.CurrentProfile.WorldMapShowMarkersNames = _showMarkerNames;

            ProfileManager.CurrentProfile.WorldMapHiddenMarkerFiles = string.Join(",", _hiddenMarkerFiles);
            ProfileManager.CurrentProfile.WorldMapHiddenZoneFiles = string.Join(",", _hiddenZoneFiles);

            ProfileManager.CurrentProfile.WorldMapShowGridIfZoomed = _showGridIfZoomed;
            ProfileManager.CurrentProfile.WorldMapAllowPositionalTarget = _allowPositionalTarget;
        }

        private bool ParseBool(string boolStr)
        {
            return bool.TryParse(boolStr, out var value) && value;
        }

        private void BuildGump()
        {
            BuildContextMenu();
        }

        private void BuildOptionDictionary()
        {
            _options.Clear();

            _options["show_all_markers"] = new ContextMenuItemEntry(ResGumps.ShowAllMarkers, () =>
            {
                _showMarkers = !_showMarkers;

                SaveSettings();
            }, true, _showMarkers);

            _options["show_marker_names"] = new ContextMenuItemEntry(ResGumps.ShowMarkerNames, () =>
            {
                _showMarkerNames = !_showMarkerNames;

                SaveSettings();
            }, true, _showMarkerNames);

            _options["show_marker_icons"] = new ContextMenuItemEntry(ResGumps.ShowMarkerIcons, () =>
            {
                _showMarkerIcons = !_showMarkerIcons;

                SaveSettings();
            }, true, _showMarkerIcons);

            _options["flip_map"] = new ContextMenuItemEntry(ResGumps.FlipMap, () =>
            {
                _flipMap = !_flipMap;

                SaveSettings();
            }, true, _flipMap);

            _options["goto_location"] = new ContextMenuItemEntry(ResGumps.GotoLocation, () =>
            {
                var dialog = new EntryDialog(250, 150, ResGumps.EnterLocation, name =>
                {
                    if (_gotoMarker != null)
                    {
                        _gotoMarker.Name = null;
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        GameActions.Print(ResGumps.InvalidLocation, 0x35);

                        return;
                    }

                    var x = -1;
                    var y = -1;

                    var coords = name.Split(' ');

                    if (coords.Length < 2)
                    {
                        try
                        {
                            ConvertCoords(name, ref x, ref y);
                        }
                        catch
                        {
                            GameActions.Print(ResGumps.InvalidLocation, 0x35);
                        }
                    }
                    else
                    {
                        if (!int.TryParse(coords[0], out x))
                        {
                            GameActions.Print(ResGumps.InvalidLocation, 0x35);
                        }

                        if (!int.TryParse(coords[1], out y))
                        {
                            GameActions.Print(ResGumps.InvalidLocation, 0x35);
                        }
                    }

                    if (x != -1 && y != -1)
                    {
                        GoToMarker(x, y, true);
                    }
                })
                {
                    CanCloseWithRightClick = true
                };

                UIManager.Add(dialog);
            });

            _options["top_most"] = new ContextMenuItemEntry(ResGumps.TopMost, () =>
            {
                TopMost = !TopMost;
                SaveSettings();
            }, true, _isTopMost);

            _options["free_view"] = new ContextMenuItemEntry(ResGumps.FreeView, () =>
            {
                FreeView = !FreeView;
                SaveSettings();
            }, true, FreeView);

            _options["show_party_members"] = new ContextMenuItemEntry(ResGumps.ShowPartyMembers, () =>
            {
                _showPartyMembers = !_showPartyMembers;

                World.WMapManager.SetEnable(_showPartyMembers);

                SaveSettings();
            }, true, _showPartyMembers);

            _options["show_mobiles"] = new ContextMenuItemEntry(ResGumps.ShowMobiles, () =>
            {
                _showMobiles = !_showMobiles;

                SaveSettings();
            }, true, _showMobiles);

            _options["show_multis"] = new ContextMenuItemEntry(ResGumps.ShowHousesBoats, () =>
            {
                _showMultis = !_showMultis;

                SaveSettings();
            }, true, _showMultis);

            _options["show_your_name"] = new ContextMenuItemEntry(ResGumps.ShowYourName, () =>
            {
                _showPlayerName = !_showPlayerName;

                SaveSettings();
            }, true, _showPlayerName);

            _options["show_your_healthbar"] = new ContextMenuItemEntry(ResGumps.ShowYourHealthbar, () =>
            {
                _showPlayerBar = !_showPlayerBar;

                SaveSettings();
            }, true, _showPlayerBar);

            _options["show_party_name"] = new ContextMenuItemEntry(ResGumps.ShowGroupName, () =>
            {
                _showGroupName = !_showGroupName;

                SaveSettings();
            }, true, _showGroupName);

            _options["show_party_healthbar"] = new ContextMenuItemEntry(ResGumps.ShowGroupHealthbar, () =>
            {
                _showGroupBar = !_showGroupBar;

                SaveSettings();
            }, true, _showGroupBar);

            _options["show_coordinates"] = new ContextMenuItemEntry(ResGumps.ShowYourCoordinates, () =>
            {
                _showCoordinates = !_showCoordinates;

                SaveSettings();
            }, true, _showCoordinates);

            _options["show_mouse_coordinates"] = new ContextMenuItemEntry(ResGumps.ShowMouseCoordinates, () =>
            {
                _showMouseCoordinates = !_showMouseCoordinates;

                SaveSettings();
            }, true, _showMouseCoordinates);

            _options["allow_positional_target"] = new ContextMenuItemEntry(ResGumps.AllowPositionalTargeting, () =>
            {
                _allowPositionalTarget = !_allowPositionalTarget;

                SaveSettings();
            }, true, _allowPositionalTarget);

            _options["markers_manager"] = new ContextMenuItemEntry(ResGumps.MarkersManager, () =>
            {
                var mm = UIManager.GetGump<MarkersManagerGump>();

                if (mm != null)
                {
                    mm.BringOnTop();
                    mm.RequestUpdateContents();
                }
                else
                {
                    UIManager.Add(new MarkersManagerGump());
                }
            });

            _options["add_marker_on_player"] = new ContextMenuItemEntry(ResGumps.AddMarkerOnPlayer, AddMarkerOnPlayer);
            _options["saveclose"] = new ContextMenuItemEntry(ResGumps.SaveClose, Dispose);

            _options["show_grid_if_zoomed"] = new ContextMenuItemEntry(ResGumps.GridIfZoomed, () =>
            {
                _showGridIfZoomed = !_showGridIfZoomed;

                SaveSettings();
            }, true, _showGridIfZoomed);
        }

        public void GoToMarker(int x, int y, bool isManual)
        {
            FreeView = true;

            _gotoMarker ??= new WMapMarker()
            {
                ColorName = "marine",
                ZoomIndex = 1,
            };

            _gotoMarker.X = x;
            _gotoMarker.Y = y;
            _gotoMarker.MapId = World.MapIndex;
            _gotoMarker.Name = isManual ? $"Go to: {x}, {y}" : string.Empty;

            _center.X = x;
            _center.Y = y;
        }

        private void BuildContextMenuForZones(ContextMenuControl parent)
        {
            var zoneOptions = new ContextMenuItemEntry(ResGumps.MapZoneOptions);

            zoneOptions.Add(_options["show_grid_if_zoomed"]);

            zoneOptions.Add(new ContextMenuItemEntry(ResGumps.MapZoneReload, () =>
            {
                LoadZones();
                BuildContextMenu();
            }));

            zoneOptions.Add(new ContextMenuItemEntry(string.Empty));

            if (_zoneSets.ZoneSetDict.Count < 1)
            {
                zoneOptions.Add(new ContextMenuItemEntry(ResGumps.MapZoneNone));

                parent.Add(zoneOptions);

                return;
            }

            foreach (KeyValuePair<string, ZoneSet> entry in _zoneSets.ZoneSetDict)
            {
                var filename = entry.Key;
                var zoneSet = entry.Value;

                zoneOptions.Add(new ContextMenuItemEntry(string.Format(ResGumps.MapZoneFileName, zoneSet.NiceFileName), () =>
                {
                    zoneSet.Hidden = !zoneSet.Hidden;

                    if (!zoneSet.Hidden)
                    {
                        var hiddenFile = _hiddenZoneFiles.FirstOrDefault(x => x.Equals(filename));

                        if (!string.IsNullOrEmpty(hiddenFile))
                        {
                            _hiddenZoneFiles.Remove(hiddenFile);
                        }
                    }
                    else
                    {
                        _hiddenZoneFiles.Add(filename);
                    }
                }, true, !entry.Value.Hidden));
            }

            parent.Add(zoneOptions);
        }

        private void BuildContextMenu()
        {
            BuildOptionDictionary();

            ContextMenu?.Dispose();
            ContextMenu = new ContextMenuControl();

            var markerFontEntry = new ContextMenuItemEntry(ResGumps.FontStyle);

            markerFontEntry.Add(new ContextMenuItemEntry(string.Format(ResGumps.Style0, 1), () => SetFont(1)));
            markerFontEntry.Add(new ContextMenuItemEntry(string.Format(ResGumps.Style0, 2), () => SetFont(2)));
            markerFontEntry.Add(new ContextMenuItemEntry(string.Format(ResGumps.Style0, 3), () => SetFont(3)));
            markerFontEntry.Add(new ContextMenuItemEntry(string.Format(ResGumps.Style0, 4), () => SetFont(4)));
            markerFontEntry.Add(new ContextMenuItemEntry(string.Format(ResGumps.Style0, 5), () => SetFont(5)));
            markerFontEntry.Add(new ContextMenuItemEntry(string.Format(ResGumps.Style0, 6), () => SetFont(6)));

            var markersEntry = new ContextMenuItemEntry(ResGumps.MapMarkerOptions);

            markersEntry.Add(new ContextMenuItemEntry(ResGumps.ReloadMarkers, LoadMarkers));

            markersEntry.Add(markerFontEntry);

            markersEntry.Add(_options["show_all_markers"]);
            markersEntry.Add(new ContextMenuItemEntry(string.Empty));
            markersEntry.Add(_options["show_marker_names"]);
            markersEntry.Add(_options["show_marker_icons"]);
            markersEntry.Add(new ContextMenuItemEntry(string.Empty));

            if (MarkerFiles.Count > 0)
            {
                foreach (var markerFile in MarkerFiles.Values)
                {
                    var entry = new ContextMenuItemEntry(string.Format(ResGumps.ShowHide0, markerFile.Name), () =>
                    {
                        markerFile.Hidden = !markerFile.Hidden;

                        if (!markerFile.Hidden)
                        {
                            var hiddenFile = _hiddenMarkerFiles.SingleOrDefault(x => x.Equals(markerFile.Name));

                            if (!string.IsNullOrEmpty(hiddenFile))
                            {
                                _hiddenMarkerFiles.Remove(hiddenFile);
                            }
                        }
                        else
                        {
                            _hiddenMarkerFiles.Add(markerFile.Name);
                        }
                    }, true, !markerFile.Hidden);

                    _options[$"show_marker_{markerFile.Name}"] = entry;

                    markersEntry.Add(entry);
                }
            }
            else
            {
                markersEntry.Add(new ContextMenuItemEntry(ResGumps.NoMapFiles));
            }

            ContextMenu.Add(markersEntry);

            BuildContextMenuForZones(ContextMenu);

            var namesHpBarEntry = new ContextMenuItemEntry(ResGumps.NamesHealthbars);

            namesHpBarEntry.Add(_options["show_your_name"]);
            namesHpBarEntry.Add(_options["show_your_healthbar"]);
            namesHpBarEntry.Add(_options["show_party_name"]);
            namesHpBarEntry.Add(_options["show_party_healthbar"]);

            ContextMenu.Add(namesHpBarEntry);

            ContextMenu.Add(string.Empty, null);
            ContextMenu.Add(_options["goto_location"]);
            ContextMenu.Add(_options["flip_map"]);
            ContextMenu.Add(_options["top_most"]);
            ContextMenu.Add(_options["free_view"]);
            ContextMenu.Add(string.Empty, null);
            ContextMenu.Add(_options["show_party_members"]);
            ContextMenu.Add(_options["show_mobiles"]);
            ContextMenu.Add(_options["show_multis"]);
            ContextMenu.Add(_options["show_coordinates"]);
            ContextMenu.Add(_options["show_mouse_coordinates"]);
            ContextMenu.Add(_options["allow_positional_target"]);
            ContextMenu.Add(string.Empty, null);
            ContextMenu.Add(_options["markers_manager"]);
            ContextMenu.Add(_options["add_marker_on_player"]);
            ContextMenu.Add(string.Empty, null);
            ContextMenu.Add(_options["saveclose"]);
        }

        public override void Update()
        {
            base.Update();

            if (IsDisposed)
            {
                return;
            }

            if (_mapIndex != World.MapIndex)
            {
                Load();
            }

            World.WMapManager.RequestServerPartyGuildInfo();
        }

        private Point RotatePoint(int x, int y, float zoom, int dist, float angle = 45f)
        {
            x = (int)(x * zoom);
            y = (int)(y * zoom);

            if (angle == 0.0f)
            {
                return new Point(x, y);
            }

            var cos = Math.Cos(dist * Math.PI / 4.0);
            var sin = Math.Sin(dist * Math.PI / 4.0);

            return new Point((int)Math.Round(cos * x - sin * y), (int)Math.Round(sin * x + cos * y));
        }

        private void AdjustPosition(int x, int y, int centerX, int centerY, out int newX, out int newY)
        {
            var offset = GetOffset(x, y, centerX, centerY);
            var currX = x;
            var currY = y;

            while (offset != 0)
            {
                if ((offset & 1) != 0)
                {
                    currY = centerY;
                    currX = x * currY / y;
                }
                else if ((offset & 2) != 0)
                {
                    currY = -centerY;
                    currX = x * currY / y;
                }
                else if ((offset & 4) != 0)
                {
                    currX = centerX;
                    currY = y * currX / x;
                }
                else if ((offset & 8) != 0)
                {
                    currX = -centerX;
                    currY = y * currX / x;
                }

                x = currX;
                y = currY;
                offset = GetOffset(x, y, centerX, centerY);
            }

            newX = x;
            newY = y;
        }

        private void CanvasToWorld(int a_x, int a_y, out int out_x, out int out_y)
        {
            // Scale width to Zoom
            var newWidth = Width / Zoom;
            var newHeight = Height / Zoom;

            // Scale mouse cords to Zoom
            var newX = a_x / Zoom;
            var newY = a_y / Zoom;

            // Rotate Cords if map fliped
            // x' = (x + y)/Sqrt(2)
            // y' = (y - x)/Sqrt(2)
            if (_flipMap)
            {
                newWidth = (int)((newWidth + newHeight) / 1.41f);
                newHeight = (int)((newHeight - newWidth) / 1.41f);

                newX = (int)((newX + newY) / 1.41f);
                newY = (int)((newY - newX) / 1.41f);
            }

            // Calulate Click cords to Map Cords
            // (x,y) = MapCenter - ScaeldMapWidth/2 + ScaledMouseCords
            out_x = _center.X - (int)(newWidth / 2) + (int)newX;
            out_y = _center.Y - (int)(newHeight / 2) + (int)newY;
        }

        private int GetOffset(int x, int y, int centerX, int centerY)
        {
            if (y > centerY)
            {
                return 1;
            }

            if (y < -centerY)
            {
                return 2;
            }

            if (x > centerX)
            {
                return 4;
            }

            if (x >= -centerX)
            {
                return 0;
            }

            return 8;
        }

        private void HandlePositionTarget()
        {
            var position = Mouse.Position;

            int x = position.X - X - ParentX;
            int y = position.Y - Y - ParentY;

            CanvasToWorld(x, y, out int xMap, out int yMap);

            TargetManager.Target(0, (ushort)xMap, (ushort)yMap, World.Map.GetTileZ(xMap, yMap));
        }

        public override void Dispose()
        {
            SaveSettings();

            World.WMapManager.SetEnable(false);

            Client.Game.GameCursor.IsDraggingCursorForced = false;

            base.Dispose();
        }

        private void SetFont(int fontIndex)
        {
            _markerFontIndex = fontIndex;

            switch (fontIndex)
            {
                case 1:
                {
                    _markerFont = Fonts.Map1;
                    break;
                }

                case 2:
                {
                    _markerFont = Fonts.Map2;
                    break;
                }

                case 3:
                {
                    _markerFont = Fonts.Map3;
                    break;
                }

                case 4:
                {
                    _markerFont = Fonts.Map4;
                    break;
                }

                case 5:
                {
                    _markerFont = Fonts.Map5;
                    break;
                }

                case 6:
                {
                    _markerFont = Fonts.Map6;
                    break;
                }

                default:
                {
                    _markerFontIndex = 1;
                    _markerFont = Fonts.Map1;
                    break;
                }
            }
        }

        private bool GetOptionValue(string key)
        {
            _options.TryGetValue(key, out ContextMenuItemEntry v);

            return v != null && v.IsSelected;
        }

        public void SetOptionValue(string key, bool v)
        {
            if (_options.TryGetValue(key, out ContextMenuItemEntry entry) && entry != null)
            {
                entry.IsSelected = v;
            }
        }

        internal class WMapMarker
        {
            public int X { get; set; }
            public int Y { get; set; }

            public int MapId { get; set; }

            public string Name { get; set; }

            public string IconName { get; set; }
            public string ColorName { get; set; } = "white";

            public int ZoomIndex { get; set; } = 3;

            public Texture2D Icon => GetIcon(IconName);
            public Color Color => GetColor(ColorName);

            public string ToCSV()
            {
                return $"{X},{Y},{MapId},{Name},{IconName},{ColorName},{ZoomIndex}";
            }
        }

        internal class WMapMarkerFile
        {
            public string Name { get; set; }
            public string FullPath { get; set; }
            public List<WMapMarker> Markers { get; set; }
            public bool Hidden { get; set; }
            public bool IsEditable { get; set; }
        }

        private class CurLoader
        {
            public static unsafe Texture2D CreateTextureFromICO_Cur(Stream stream)
            {
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent((int)stream.Length);

                try
                {
                    stream.Read(buffer, 0, buffer.Length);

                    var reader = new StackDataReader(buffer.AsSpan(0, (int)stream.Length));

                    int bmp_pitch, i, pad;
                    SDL.SDL_Surface* surface;
                    byte* bits;
                    int expand_bmp, max_col = 0;
                    uint ico_of_s = 0;
                    uint* palette = stackalloc uint[256];

                    ushort bf_reserved, bf_type, bf_count;
                    uint bi_size, bi_width, bi_height;

                    ushort bi_planes, bi_bit_count;
                    uint bi_compression, bi_size_image, bi_x_perls_per_meter, bi_y_perls_per_meter, bi_clr_used, bi_clr_important;

                    bf_reserved = reader.ReadUInt16LE();
                    bf_type = reader.ReadUInt16LE();
                    bf_count = reader.ReadUInt16LE();

                    for (i = 0; i < bf_count; i++)
                    {
                        var b_width = (int)reader.ReadUInt8();
                        var b_height = (int)reader.ReadUInt8();
                        var b_color_count = (int)reader.ReadUInt8();
                        var b_reserver = reader.ReadUInt8();
                        var w_planes = reader.ReadUInt16LE();
                        var w_bit_count = reader.ReadUInt16LE();
                        var dw_bytes_in_res = reader.ReadUInt32LE();
                        var dw_image_offse = reader.ReadUInt32LE();

                        if (b_width == 0)
                        {
                            b_width = 256;
                        }

                        if (b_height == 0)
                        {
                            b_height = 256;
                        }

                        if (b_color_count == 0)
                        {
                            b_color_count = 256;
                        }

                        if (b_color_count > max_col)
                        {
                            max_col = b_color_count;
                            ico_of_s = dw_image_offse;
                        }
                    }

                    reader.Seek(ico_of_s);

                    bi_size = reader.ReadUInt32LE();

                    if (bi_size != 40)
                    {
                        return null;
                    }

                    bi_width = reader.ReadUInt32LE();
                    bi_height = reader.ReadUInt32LE();
                    bi_planes = reader.ReadUInt16LE();
                    bi_bit_count = reader.ReadUInt16LE();
                    bi_compression = reader.ReadUInt32LE();
                    bi_size_image = reader.ReadUInt32LE();
                    bi_x_perls_per_meter = reader.ReadUInt32LE();
                    bi_y_perls_per_meter = reader.ReadUInt32LE();
                    bi_clr_used = reader.ReadUInt32LE();
                    bi_clr_important = reader.ReadUInt32LE();

                    const int BI_RGB = 0;

                    switch (bi_compression)
                    {
                        case BI_RGB:
                        {
                            switch (bi_bit_count)
                            {
                                case 1:
                                case 4:
                                {
                                    expand_bmp = bi_bit_count;
                                    bi_bit_count = 8;
                                    break;
                                }

                                case 8:
                                {
                                    expand_bmp = 8;
                                    break;
                                }

                                case 32:
                                {
                                    expand_bmp = 0;
                                    break;
                                }

                                default:
                                {
                                    return null;
                                }
                            }

                            break;
                        }

                        default:
                        {
                            return null;
                        }
                    }

                    bi_height >>= 1;

                    surface = (SDL.SDL_Surface*)SDL.SDL_CreateRGBSurface(0, (int)bi_width, (int)bi_height, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000);

                    if (bi_bit_count <= 8)
                    {
                        if (bi_clr_used == 0)
                        {
                            bi_clr_used = (uint)(1 << bi_bit_count);
                        }

                        for (i = 0; i < bi_clr_used; i++)
                        {
                            palette[i] = reader.ReadUInt32LE();
                        }
                    }

                    bits = (byte*)(surface->pixels + surface->h * surface->pitch);

                    switch (expand_bmp)
                    {
                        case 1:
                        {
                            bmp_pitch = (int)(bi_width + 7) >> 3;
                            pad = bmp_pitch % 4 != 0 ? 4 - bmp_pitch % 4 : 0;
                            break;
                        }

                        case 4:
                        {
                            bmp_pitch = (int)(bi_width + 1) >> 1;
                            pad = bmp_pitch % 4 != 0 ? 4 - bmp_pitch % 4 : 0;
                            break;
                        }

                        case 8:
                        {
                            bmp_pitch = (int)bi_width;
                            pad = bmp_pitch % 4 != 0 ? 4 - bmp_pitch % 4 : 0;
                            break;
                        }

                        default:
                        {
                            bmp_pitch = (int)bi_width * 4;
                            pad = 0;
                            break;
                        }
                    }

                    while (bits > (byte*)surface->pixels)
                    {
                        bits -= surface->pitch;

                        switch (expand_bmp)
                        {
                            case 1:
                            case 4:
                            case 8:
                            {
                                var pixel = 0;
                                var shift = 8 - expand_bmp;

                                for (i = 0; i < surface->w; i++)
                                {
                                    if (i % (8 / expand_bmp) == 0)
                                    {
                                        pixel = reader.ReadUInt8();
                                    }

                                    *((uint*)bits + i) = palette[pixel >> shift];

                                    pixel <<= expand_bmp;
                                }

                                break;
                            }

                            default:
                            {
                                for (var k = 0; k < surface->pitch; k++)
                                {
                                    bits[k] = reader.ReadUInt8();
                                }

                                break;
                            }
                        }

                        if (pad != 0)
                        {
                            for (i = 0; i < pad; i++)
                            {
                                reader.ReadUInt8();
                            }
                        }
                    }

                    bits = (byte*)(surface->pixels + surface->h * surface->pitch);
                    expand_bmp = 1;
                    bmp_pitch = (int)(bi_width + 7) >> 3;
                    pad = bmp_pitch % 4 != 0 ? 4 - bmp_pitch % 4 : 0;

                    while (bits > (byte*)surface->pixels)
                    {
                        var pixel = 0;
                        var shift = 8 - expand_bmp;

                        bits -= surface->pitch;

                        for (i = 0; i < surface->w; i++)
                        {
                            if (i % (8 / expand_bmp) == 0)
                            {
                                pixel = reader.ReadUInt8();
                            }

                            *((uint*)bits + i) |= pixel >> shift != 0 ? 0 : 0xFF000000;

                            pixel <<= expand_bmp;
                        }

                        if (pad != 0)
                        {
                            for (i = 0; i < pad; i++)
                            {
                                reader.ReadUInt8();
                            }
                        }
                    }

                    surface = (SDL.SDL_Surface*)INTERNAL_convertSurfaceFormat((IntPtr)surface);

                    var len = surface->w * surface->h * 4;
                    var pixels = (byte*)surface->pixels;

                    for (i = 0; i < len; i += 4, pixels += 4)
                    {
                        if (pixels[3] == 0)
                        {
                            pixels[0] = 0;
                            pixels[1] = 0;
                            pixels[2] = 0;
                        }
                    }

                    var texture = new Texture2D(Client.Game.GraphicsDevice, surface->w, surface->h);

                    texture.SetDataPointerEXT(0, new Rectangle(0, 0, surface->w, surface->h), surface->pixels, len);

                    SDL.SDL_FreeSurface((IntPtr)surface);

                    reader.Release();

                    return texture;
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            private static unsafe IntPtr INTERNAL_convertSurfaceFormat(IntPtr surface)
            {
                IntPtr result = surface;
                SDL.SDL_Surface* surPtr = (SDL.SDL_Surface*)surface;
                SDL.SDL_PixelFormat* pixelFormatPtr = (SDL.SDL_PixelFormat*)surPtr->format;

                // SurfaceFormat.Color is SDL_PIXELFORMAT_ABGR8888
                if (pixelFormatPtr->format != SDL.SDL_PIXELFORMAT_ABGR8888)
                {
                    // Create a properly formatted copy, free the old surface
                    result = SDL.SDL_ConvertSurfaceFormat(surface, SDL.SDL_PIXELFORMAT_ABGR8888, 0);

                    SDL.SDL_FreeSurface(surface);
                }

                return result;
            }
        }

        #region Loading

        private unsafe void LoadMapChunk(Span<uint> buffer, Span<sbyte> allZ, int chunkX, int chunkY)
        {
            if (World.Map == null)
            {
                return;
            }

            var huesLoader = HuesLoader.Instance;

            ref var indexMap = ref World.Map.GetIndex(chunkX, chunkY);

            if (indexMap.MapAddress == 0)
            {
                return;
            }

            var block = 0;

            var mapBlock = (MapBlock*)indexMap.MapAddress;
            var cells = (MapCells*)&mapBlock->Cells;

            for (var y = 0; y < 8; ++y)
            {
                var pos = y << 3;

                for (int x = 0; x < 8; ++x, ++pos, ++block)
                {
                    var color = (ushort)(0x8000 | huesLoader.GetRadarColorData(cells[pos].TileID & 0x3FFF));

                    buffer[block] = HuesHelper.Color16To32(color) | 0xFF_00_00_00;
                    allZ[block] = cells[pos].Z;
                }
            }

            var sb = (StaticsBlock*)indexMap.StaticAddress;

            if (sb != null)
            {
                var count = (int)indexMap.StaticCount;

                for (var c = 0; c < count; ++c, ++sb)
                {
                    if (sb->Color != 0 && sb->Color != 0xFFFF && GameObject.CanBeDrawn(sb->Color))
                    {
                        var index = sb->Y * 8 + sb->X;

                        if (sb->Z >= allZ[index])
                        {
                            var color = (ushort)(0x8000 | (sb->Hue != 0 ? huesLoader.GetColor16(16384, sb->Hue) : huesLoader.GetRadarColorData(sb->Color + 0x4000)));

                            buffer[index] = HuesHelper.Color16To32(color) | 0xFF_00_00_00;
                            allZ[index] = sb->Z;
                        }
                    }
                }
            }
        }

        private void LoadMapDetails(Span<uint> buffer, Span<sbyte> allZ)
        {
            const float MAG_0 = 80f / 100f;
            const float MAG_1 = 100f / 80f;

            for (var mapY = 0; mapY < 8; ++mapY)
            {
                var index = mapY * 8;
                var indexNextRow = (mapY + 1) * 8;

                for (var mapX = 0; mapX < 8; ++mapX, ++index, ++indexNextRow)
                {
                    var z0 = allZ[index];
                    var z1 = allZ[(indexNextRow >= allZ.Length ? (index + 0) : indexNextRow) % allZ.Length];

                    ref var cc = ref buffer[index];

                    if (z0 == z1 || cc == 0)
                    {
                        continue;
                    }

                    var r = (byte)(cc & 0xFF);
                    var g = (byte)((cc >> 8) & 0xFF);
                    var b = (byte)((cc >> 16) & 0xFF);
                    var a = (byte)((cc >> 24) & 0xFF);

                    if (z0 < z1)
                    {
                        r = (byte)Math.Min(0xFF, r * MAG_0);
                        g = (byte)Math.Min(0xFF, g * MAG_0);
                        b = (byte)Math.Min(0xFF, b * MAG_0);
                    }
                    else
                    {
                        r = (byte)Math.Min(0xFF, r * MAG_1);
                        g = (byte)Math.Min(0xFF, g * MAG_1);
                        b = (byte)Math.Min(0xFF, b * MAG_1);
                    }

                    cc = (uint)(r | (g << 8) | (b << 16) | (a << 24));
                }
            }
        }

        private unsafe Task Load()
        {
            _mapIndex = World.MapIndex;

            if (_mapIndex < 0 || _mapIndex > MapLoader.MAPS_COUNT)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                if (World.InGame)
                {
                    const int OFFSET_PIX = 2;
                    const int OFFSET_PIX_HALF = OFFSET_PIX / 2;

                    if (_mapTexture == null || _mapTexture.IsDisposed)
                    {
                        var maxX = -1;
                        var maxY = -1;

                        for (var i = 0; i < MapLoader.Instance.MapsDefaultSize.GetLength(0); i++)
                        {
                            if (maxX < MapLoader.Instance.MapsDefaultSize[i, 0])
                            {
                                maxX = MapLoader.Instance.MapsDefaultSize[i, 0];
                            }

                            if (maxY < MapLoader.Instance.MapsDefaultSize[i, 1])
                            {
                                maxY = MapLoader.Instance.MapsDefaultSize[i, 1];
                            }
                        }

                        if (OFFSET_PIX > 0)
                        {
                            maxX += OFFSET_PIX;
                            maxY += OFFSET_PIX;
                        }

                        _mapTexture = new Texture2D(Client.Game.GraphicsDevice, maxX, maxY, false, SurfaceFormat.Color);
                        _pixelBuffer = new uint[maxX * maxY];
                        _zBuffer = new sbyte[maxX * maxY];
                    }

                    try
                    {
                        var realWidth = MapLoader.Instance.MapsDefaultSize[World.MapIndex, 0];
                        var realHeight = MapLoader.Instance.MapsDefaultSize[World.MapIndex, 1];

                        var fixedWidth = MapLoader.Instance.MapBlocksSize[World.MapIndex, 0];
                        var fixedHeight = MapLoader.Instance.MapBlocksSize[World.MapIndex, 1];

                        var size = (realWidth + OFFSET_PIX) * (realHeight + OFFSET_PIX);

                        var allZ = _zBuffer;
                        var buffer = _pixelBuffer;

                        // horrible tweak to cleanup texture... but works!
                        buffer.AsSpan().Clear();

                        fixed (uint* pixels = &buffer[0])
                        {
                            _mapTexture.SetDataPointerEXT(0, null, (IntPtr)pixels, sizeof(uint) * _mapTexture.Width * _mapTexture.Height);
                        }

                        var huesLoader = HuesLoader.Instance;

                        int bx, by, mapX = 0, mapY = 0, x, y;

                        for (bx = 0; bx < fixedWidth; ++bx)
                        {
                            mapX = bx << 3;

                            for (by = 0; by < fixedHeight; ++by)
                            {
                                ref var indexMap = ref World.Map.GetIndex(bx, by);

                                if (indexMap.MapAddress == 0)
                                {
                                    continue;
                                }

                                var mapBlock = (MapBlock*)indexMap.MapAddress;
                                var cells = (MapCells*)&mapBlock->Cells;

                                mapY = by << 3;

                                for (y = 0; y < 8; ++y)
                                {
                                    var block = (mapY + y + OFFSET_PIX_HALF) * (realWidth + OFFSET_PIX) + mapX + OFFSET_PIX_HALF;

                                    var pos = y << 3;

                                    for (x = 0; x < 8; ++x, ++pos, ++block)
                                    {
                                        var color = (ushort)(0x8000 | huesLoader.GetRadarColorData(cells[pos].TileID & 0x3FFF));

                                        buffer[block] = HuesHelper.Color16To32(color) | 0xFF_00_00_00;
                                        allZ[block] = cells[pos].Z;
                                    }
                                }

                                var sb = (StaticsBlock*)indexMap.StaticAddress;

                                if (sb != null)
                                {
                                    var count = (int)indexMap.StaticCount;

                                    for (var c = 0; c < count; ++c, ++sb)
                                    {
                                        if (sb->Color != 0 && sb->Color != 0xFFFF && GameObject.CanBeDrawn(sb->Color))
                                        {
                                            var block = (mapY + sb->Y + OFFSET_PIX_HALF) * (realWidth + OFFSET_PIX) + mapX + sb->X + OFFSET_PIX_HALF;

                                            if (sb->Z >= allZ[block])
                                            {
                                                var color = (ushort)(0x8000 | (sb->Hue != 0 ? huesLoader.GetColor16(16384, sb->Hue) : huesLoader.GetRadarColorData(sb->Color + 0x4000)));

                                                buffer[block] = HuesHelper.Color16To32(color) | 0xFF_00_00_00;
                                                allZ[block] = sb->Z;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        var real_width_less_one = realWidth - 1;
                        var real_height_less_one = realHeight - 1;

                        const float MAG_0 = 80f / 100f;
                        const float MAG_1 = 100f / 80f;

                        for (mapY = 1; mapY < real_height_less_one; ++mapY)
                        {
                            var blockCurrent = (mapY + OFFSET_PIX_HALF) * (realWidth + OFFSET_PIX) + OFFSET_PIX_HALF;
                            var blockNext = (mapY + 1 + OFFSET_PIX_HALF) * (realWidth + OFFSET_PIX) + OFFSET_PIX_HALF;

                            for (mapX = 1; mapX < real_width_less_one; ++mapX)
                            {
                                var z0 = allZ[++blockCurrent];
                                var z1 = allZ[blockNext++];

                                if (z0 == z1)
                                {
                                    continue;
                                }

                                ref var cc = ref buffer[blockCurrent];

                                if (cc == 0)
                                {
                                    continue;
                                }

                                var r = (byte)(cc & 0xFF);
                                var g = (byte)((cc >> 8) & 0xFF);
                                var b = (byte)((cc >> 16) & 0xFF);
                                var a = (byte)((cc >> 24) & 0xFF);

                                if (r != 0 || g != 0 || b != 0)
                                {
                                    if (z0 < z1)
                                    {
                                        r = (byte)Math.Min(0xFF, r * MAG_0);
                                        g = (byte)Math.Min(0xFF, g * MAG_0);
                                        b = (byte)Math.Min(0xFF, b * MAG_0);
                                    }
                                    else
                                    {
                                        r = (byte)Math.Min(0xFF, r * MAG_1);
                                        g = (byte)Math.Min(0xFF, g * MAG_1);
                                        b = (byte)Math.Min(0xFF, b * MAG_1);
                                    }

                                    cc = (uint)(r | (g << 8) | (b << 16) | (a << 24));
                                }
                            }
                        }

                        if (OFFSET_PIX > 0)
                        {
                            realWidth += OFFSET_PIX;
                            realHeight += OFFSET_PIX;
                        }

                        fixed (uint* pixels = &buffer[0])
                        {
                            _mapTexture.SetDataPointerEXT(0, new Rectangle(0, 0, realWidth, realHeight), (IntPtr)pixels, sizeof(uint) * realWidth * realHeight);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"error loading worldmap: {ex}");
                    }

                    GameActions.Print(ResGumps.WorldMapLoaded, 0x48);
                }
            });
        }

        internal class ZonesFileZoneData
        {
            public string Label { get; set; }

            public string Color { get; set; }

            public List<List<int>> Polygon { get; set; }
        }

        internal class ZonesFile
        {
            public int MapIndex { get; set; }
            public List<ZonesFileZoneData> Zones { get; set; }
        }

        private class Zone
        {
            public string Label;
            public Color Color;
            public Rectangle BoundingRectangle;
            public List<Point> Vertices;

            public Zone(ZonesFileZoneData data)
            {
                Label = data.Label;
                Color = GetColor(data.Color);

                Vertices = [];

                int xmin = int.MaxValue;
                int xmax = int.MinValue;
                int ymin = int.MaxValue;
                int ymax = int.MinValue;

                foreach (var rawPoint in data.Polygon)
                {
                    var p = new Point(rawPoint[0], rawPoint[1]);

                    if (p.X < xmin)
                    {
                        xmin = p.X;
                    }

                    if (p.X > xmax)
                    {
                        xmax = p.X;
                    }

                    if (p.Y < ymin)
                    {
                        ymin = p.Y;
                    }

                    if (p.Y > ymax)
                    {
                        ymax = p.Y;
                    }

                    Vertices.Add(p);
                }

                BoundingRectangle = new Rectangle(xmin, ymin, xmax - xmin, ymax - ymin);
            }
        }

        private class ZoneSet
        {
            public int MapIndex;
            public List<Zone> Zones = [];
            public bool Hidden = false;
            public string NiceFileName;

            public ZoneSet(ZonesFile zf, string filename, bool hidden)
            {
                MapIndex = zf.MapIndex;

                foreach (var data in zf.Zones)
                {
                    Zones.Add(new Zone(data));
                }

                Hidden = hidden;
                NiceFileName = MakeNiceFileName(filename);
            }

            public static string MakeNiceFileName(string filename)
            {
                // Yes, we invoke the same method twice, because our filenames have two layers of extension
                // we want to strip off (.zones.json)
                return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filename));
            }
        }

        private class ZoneSets
        {
            public Dictionary<string, ZoneSet> ZoneSetDict { get; } = new Dictionary<string, ZoneSet>(StringComparer.OrdinalIgnoreCase);

            public void AddZoneSetByFileName(string filename, bool hidden)
            {
                try
                {
                    var zf = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(filename), ZonesJsonContext.Default.ZonesFile);

                    ZoneSetDict[filename] = new ZoneSet(zf, filename, hidden);

                    GameActions.Print(string.Format(ResGumps.MapZoneFileLoaded, ZoneSetDict[filename].NiceFileName), 0x3A /* yellow green */);
                }
                catch (Exception ee)
                {
                    Log.Error($"{ee}");
                }
            }

            public IEnumerable<Zone> GetZonesForMapIndex(int mapIndex)
            {
                foreach (var entry in ZoneSetDict)
                {
                    if (entry.Value.MapIndex != mapIndex)
                    {
                        continue;
                    }

                    if (entry.Value.Hidden)
                    {
                        continue;
                    }

                    foreach (Zone zone in entry.Value.Zones)
                    {
                        yield return zone;
                    }
                }
            }

            public void Clear()
            {
                ZoneSetDict.Clear();
            }
        }

        private void LoadZones()
        {
            Log.Trace("LoadZones()...");

            _zoneSets.Clear();

            foreach (var filename in Directory.EnumerateFiles(_mapFilesPath, "*.zones.json"))
            {
                var shouldHide = !string.IsNullOrEmpty(_hiddenZoneFiles.FirstOrDefault(x => x.Contains(filename)));

                _zoneSets.AddZoneSetByFileName(filename, shouldHide);
            }
        }

        private bool ShouldDrawGrid()
        {
            return _showGridIfZoomed && Zoom >= 4;
        }

        private void LoadMarkers()
        {
            if (World.InGame)
            {
                _mapMarkersLoaded = false;

                GameActions.Print(ResGumps.LoadingWorldMapMarkers, 0x2A);

                foreach (Texture2D t in MarkerIcons.Values)
                {
                    if (t != null && !t.IsDisposed)
                    {
                        t.Dispose();
                    }
                }

                if (!File.Exists(UserMarkersFilePath))
                {
                    using var file = File.Create(UserMarkersFilePath);
                }

                MarkerIcons.Clear();

                Directory.CreateDirectory(_mapIconsPath);

                string[] icons =
                [
                    .. Directory.EnumerateFiles(_mapIconsPath, "*.cur"),
                    .. Directory.EnumerateFiles(_mapIconsPath, "*.ico")
                ];

                foreach (var icon in icons)
                {
                    var fs = new FileStream(icon, FileMode.Open, FileAccess.Read);
                    var ms = new MemoryStream();

                    fs.CopyTo(ms);

                    ms.Seek(0, SeekOrigin.Begin);

                    try
                    {
                        Texture2D texture = CurLoader.CreateTextureFromICO_Cur(ms);

                        MarkerIcons.Add(Path.GetFileNameWithoutExtension(icon), texture);
                    }
                    catch (Exception ee)
                    {
                        Log.Error($"{ee}");
                    }
                    finally
                    {
                        ms.Dispose();
                        fs.Dispose();
                    }
                }

                string[] images =
                [
                    .. Directory.EnumerateFiles(_mapIconsPath, "*.png"),
                    .. Directory.EnumerateFiles(_mapIconsPath, "*.jpg")
                ];

                foreach (string icon in images)
                {
                    var fs = new FileStream(icon, FileMode.Open, FileAccess.Read);
                    var ms = new MemoryStream();

                    fs.CopyTo(ms);

                    ms.Seek(0, SeekOrigin.Begin);

                    try
                    {
                        Texture2D texture = Texture2D.FromStream(Client.Game.GraphicsDevice, ms);

                        MarkerIcons.Add(Path.GetFileNameWithoutExtension(icon), texture);
                    }
                    catch (Exception ee)
                    {
                        Log.Error($"{ee}");
                    }
                    finally
                    {
                        ms.Dispose();
                        fs.Dispose();
                    }
                }

                MarkerFiles.Clear();

                string[] mapFiles =
                [
                    UserMarkersFilePath,
                    .. Directory.EnumerateFiles(_mapFilesPath, "*.map"),
                    .. Directory.EnumerateFiles(_mapFilesPath, "*.csv"),
                    .. Directory.EnumerateFiles(_mapFilesPath, "*.xml"),
                ];

                foreach (var mapFile in mapFiles)
                {
                    if (File.Exists(mapFile))
                    {
                        var markerFile = new WMapMarkerFile
                        {
                            Hidden = false,
                            Name = Path.GetFileNameWithoutExtension(mapFile),
                            FullPath = mapFile,
                            Markers = [],
                            IsEditable = false,
                        };

                        var hiddenFile = _hiddenMarkerFiles.FirstOrDefault(x => x.Contains(markerFile.Name));

                        if (!string.IsNullOrEmpty(hiddenFile))
                        {
                            markerFile.Hidden = true;
                        }

                        if (mapFile != null && Path.GetExtension(mapFile).Equals(".xml", StringComparison.OrdinalIgnoreCase)) // Ultima Mapper
                        {
                            using var reader = new XmlTextReader(File.Open(mapFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                            while (reader.Read())
                            {
                                if (reader.Name.Equals("Marker"))
                                {
                                    var marker = new WMapMarker
                                    {
                                        X = int.Parse(reader.GetAttribute("X")),
                                        Y = int.Parse(reader.GetAttribute("Y")),
                                        MapId = int.Parse(reader.GetAttribute("Facet")),
                                        Name = reader.GetAttribute("Name") ?? string.Empty,
                                        IconName = reader.GetAttribute("Icon") ?? string.Empty,
                                    };

                                    markerFile.Markers.Add(marker);
                                }
                            }
                        }
                        else if (mapFile != null && Path.GetExtension(mapFile).Equals(".map", StringComparison.OrdinalIgnoreCase)) //UOAM
                        {
                            using var reader = new StreamReader(File.Open(mapFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                            while (!reader.EndOfStream)
                            {
                                var line = reader.ReadLine();

                                // ignore empty lines, and if UOAM, ignore the first line that always has a 3
                                if (string.IsNullOrEmpty(line) || line.Equals("3"))
                                {
                                    continue;
                                }

                                // Check for UOAM file
                                if (line.Substring(0, 1).Equals("+") || line.Substring(0, 1).Equals("-"))
                                {
                                    var icon = line.Substring(1, line.IndexOf(':') - 1);

                                    line = line.Substring(line.IndexOf(':') + 2);

                                    var splits = line.Split(' ');

                                    if (splits.Length <= 1)
                                    {
                                        continue;
                                    }

                                    var iconSplits = icon.Split(' ');

                                    var marker = new WMapMarker
                                    {
                                        X = int.Parse(splits[0]),
                                        Y = int.Parse(splits[1]),
                                        MapId = int.Parse(splits[2]),
                                        Name = string.Join(" ", splits, 3, splits.Length - 3),
                                        IconName = iconSplits[0],
                                    };

                                    markerFile.Markers.Add(marker);
                                }
                            }
                        }
                        else if (mapFile != null && Path.GetExtension(mapFile).Equals(".usr", StringComparison.OrdinalIgnoreCase))
                        {
                            markerFile.Markers.Clear();
                            markerFile.Markers.AddRange(LoadUserMarkers());
                            markerFile.IsEditable = true;
                        }
                        else if (mapFile != null) //CSV x,y,mapindex,name of marker,iconname,color,zoom
                        {
                            using var reader = new StreamReader(File.Open(mapFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                            while (!reader.EndOfStream)
                            {
                                var line = reader.ReadLine();

                                if (string.IsNullOrEmpty(line))
                                {
                                    continue;
                                }

                                var splits = line.Split(',');

                                if (splits.Length <= 1)
                                {
                                    continue;
                                }

                                var marker = new WMapMarker
                                {
                                    X = int.Parse(splits[0]),
                                    Y = int.Parse(splits[1]),
                                    MapId = int.Parse(splits[2]),
                                    Name = splits[3] ?? string.Empty,
                                    IconName = splits[4],
                                    ColorName = splits[5],
                                };

                                if (splits.Length >= 7)
                                {
                                    marker.ZoomIndex = int.Parse(splits[6]);
                                }

                                markerFile.Markers.Add(marker);
                            }
                        }

                        if (markerFile.Markers.Count > 0)
                        {
                            GameActions.Print($"..{Path.GetFileName(mapFile)} ({markerFile.Markers.Count})", 0x2B);
                        }

                        MarkerFiles[markerFile.Name] = markerFile;
                    }
                }

                BuildContextMenu();

                var count = 0;

                foreach (var file in MarkerFiles.Values)
                {
                    count += file.Markers.Count;
                }

                _mapMarkersLoaded = true;

                GameActions.Print(string.Format(ResGumps.WorldMapMarkersLoaded0, count), 0x2A);
            }
        }

        private void AddMarkerOnPlayer()
        {
            if (!World.InGame)
            {
                return;
            }

            var entryDialog = new EntryDialog(250, 150, ResGumps.EnterMarkerName, SaveMakerOnPlayer)
            {
                CanCloseWithRightClick = true
            };

            UIManager.Add(entryDialog);
        }

        private void SaveMakerOnPlayer(string markerName)
        {
            if (!World.InGame)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(markerName))
            {
                GameActions.Print(ResGumps.InvalidMarkerName, 0x2A);
                return;
            }

            var marker = new WMapMarker
            {
                X = World.Player.X,
                Y = World.Player.Y,
                MapId = World.Map.Index,
                Name = markerName,
                ColorName = "blue"
            };

            AddUserMarker(marker);
        }

        public static void AddUserMarker(WMapMarker marker)
        {
            if (marker == null)
            {
                return;
            }

            var userFile = UserMarkersFile;

            if (userFile != null)
            {
                userFile.Markers ??= [];
                userFile.Markers.Add(marker);
            }

            File.AppendAllText(UserMarkersFilePath, $"{marker.ToCSV()}{Environment.NewLine}");

            if (userFile == null)
            {
                ReloadUserMarkers();
            }
        }

        public static bool RemoveUserMarker(WMapMarker marker)
        {
            if (marker == null)
            {
                return false;
            }

            var userFile = UserMarkersFile;

            if (userFile?.Markers.Remove(marker) == true)
            {
                File.WriteAllLines(UserMarkersFilePath, userFile.Markers.Select(m => m.ToCSV()));

                return true;
            }

            return false;
        }

        /// <summary>
        /// Reload User Markers File after Changes
        /// </summary>
        private static void ReloadUserMarkers()
        {
            var userFile = UserMarkersFile;

            if (userFile != null)
            {
                userFile.Markers ??= [];
                userFile.Markers.Clear();
                userFile.Markers.AddRange(LoadUserMarkers());
            }
        }

        /// <summary>
        /// Load User Markers to List of Markers
        /// </summary>
        /// <returns>List of loaded Markers</returns>
        private static IEnumerable<WMapMarker> LoadUserMarkers()
        {
            if (File.Exists(UserMarkersFilePath))
            {
                foreach (var line in File.ReadLines(UserMarkersFilePath))
                {
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    var splits = line.Split(',');

                    if (splits.Length <= 1)
                    {
                        continue;
                    }

                    var marker = new WMapMarker
                    {
                        X = int.Parse(splits[0]),
                        Y = int.Parse(splits[1]),
                        MapId = int.Parse(splits[2]),
                        Name = splits[3],
                        IconName = splits[4],
                        ColorName = splits[5],
                    };

                    if (splits.Length >= 7)
                    {
                        marker.ZoomIndex = int.Parse(splits[6]);
                    }

                    yield return marker;
                }
            }
        }

        #endregion

        #region Draw

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (IsDisposed || !World.InGame)
            {
                return false;
            }

            if (!_isScrolling && !_freeView)
            {
                _center.X = World.Player.X;
                _center.Y = World.Player.Y;
            }

            int gX = x + 4;
            int gY = y + 4;
            int gWidth = Width - 8;
            int gHeight = Height - 8;

            int centerX = _center.X + 1;
            int centerY = _center.Y + 1;

            int size = (int)Math.Max(gWidth * 1.75f, gHeight * 1.75f);

            int size_zoom = (int)(size / Zoom);
            int size_zoom_half = size_zoom >> 1;

            int halfWidth = gWidth >> 1;
            int halfHeight = gHeight >> 1;

            Vector3 hueVector = ShaderHueTranslator.GetHueVector(0);

            batcher.Draw(SolidColorTextureCache.GetTexture(Color.Black), new Rectangle(gX, gY, gWidth, gHeight), hueVector);

            if (_mapTexture != null)
            {
                if (batcher.ClipBegin(gX, gY, gWidth, gHeight))
                {
                    var destRect = new Rectangle(gX + halfWidth, gY + halfHeight, size, size);

                    var srcRect = new Rectangle(centerX - size_zoom_half, centerY - size_zoom_half, size_zoom, size_zoom);

                    var origin = new Vector2(srcRect.Width / 2f, srcRect.Height / 2f);

                    batcher.Draw(_mapTexture, destRect, srcRect, hueVector, _flipMap ? Microsoft.Xna.Framework.MathHelper.ToRadians(45) : 0, origin, SpriteEffects.None, 0);

                    DrawAll(batcher, srcRect, gX, gY, halfWidth, halfHeight);

                    batcher.ClipEnd();
                }
            }

            //foreach (House house in World.HouseManager.Houses)
            //{
            //    foreach (Multi multi in house.Components)
            //    {
            //        batcher.Draw2D(Textures.GetTexture())
            //    }
            //}

            return base.Draw(batcher, x, y);
        }

        private void DrawAll(UltimaBatcher2D batcher, Rectangle srcRect, int gX, int gY, int halfWidth, int halfHeight)
        {
            foreach (var zone in _zoneSets.GetZonesForMapIndex(World.MapIndex))
            {
                if (zone.BoundingRectangle.Intersects(srcRect))
                {
                    DrawZone(batcher, zone, gX, gY, halfWidth, halfHeight, Zoom);
                }
            }

            if (_showMultis)
            {
                foreach (var house in World.HouseManager.Houses)
                {
                    var item = World.Items.Get(house.Serial);

                    if (item != null)
                    {
                        DrawMulti(batcher, house, item.X, item.Y, gX, gY, halfWidth, halfHeight, Zoom);
                    }
                }
            }

            if (_showMarkers && _mapMarkersLoaded)
            {
                WMapMarker lastMarker = null;

                foreach (var file in MarkerFiles.Values)
                {
                    if (file.Hidden)
                    {
                        continue;
                    }

                    foreach (var marker in file.Markers)
                    {
                        if (DrawMarker(batcher, marker, gX, gY, halfWidth, halfHeight, Zoom))
                        {
                            lastMarker = marker;
                        }
                    }
                }

                if (lastMarker != null)
                {
                    DrawMarkerString(batcher, lastMarker, gX, gY, halfWidth, halfHeight);
                }
            }

            if (_gotoMarker?.Name != null)
            {
                DrawMarker(batcher, _gotoMarker, gX, gY, halfWidth, halfHeight, Zoom);
            }

            if (_showMobiles)
            {
                foreach (Mobile mob in World.Mobiles.Values)
                {
                    if (mob == World.Player)
                    {
                        continue;
                    }

                    if (mob.NotorietyFlag != NotorietyFlag.Ally)
                    {
                        DrawMobile(batcher, mob, gX, gY, halfWidth, halfHeight, Zoom, Color.Red);
                        continue;
                    }

                    if (mob != null && mob.Distance <= World.ClientViewRange)
                    {
                        var wme = World.WMapManager.GetEntity(mob);

                        if (wme == null)
                        {
                            DrawMobile(batcher, mob, gX, gY, halfWidth, halfHeight, Zoom, Color.Lime, true, true, _showGroupBar);
                            continue;
                        }

                        if (string.IsNullOrEmpty(wme.Name) && !string.IsNullOrEmpty(mob.Name))
                        {
                            wme.Name = mob.Name;
                        }
                    }
                    else
                    {
                        var wme = World.WMapManager.GetEntity(mob.Serial);

                        if (wme != null && wme.IsGuild)
                        {
                            DrawWMEntity(batcher, wme, gX, gY, halfWidth, halfHeight, Zoom);
                        }
                    }
                }
            }

            foreach (var wme in World.WMapManager.Entities.Values)
            {
                if (wme.IsGuild && !World.Party.Contains(wme.Serial))
                {
                    DrawWMEntity(batcher, wme, gX, gY, halfWidth, halfHeight, Zoom);
                }
            }

            if (_showPartyMembers)
            {
                for (var i = 0; i < 10; i++)
                {
                    var partyMember = World.Party.Members[i];

                    if (partyMember != null && SerialHelper.IsValid(partyMember.Serial))
                    {
                        var mob = World.Mobiles.Get(partyMember.Serial);

                        if (mob != null && mob.Distance <= World.ClientViewRange)
                        {
                            var wme = World.WMapManager.GetEntity(mob);

                            if (wme != null && string.IsNullOrEmpty(wme.Name) && !string.IsNullOrEmpty(partyMember.Name))
                            {
                                wme.Name = partyMember.Name;
                            }

                            DrawMobile(batcher, mob, gX, gY, halfWidth, halfHeight, Zoom, Color.Yellow, _showGroupName, true, _showGroupBar);
                        }
                        else
                        {
                            WMapEntity wme = World.WMapManager.GetEntity(partyMember.Serial);

                            if (wme != null && !wme.IsGuild)
                            {
                                DrawWMEntity(batcher, wme, gX, gY, halfWidth, halfHeight, Zoom);
                            }
                        }
                    }
                }
            }

            DrawMobile(batcher, World.Player, gX, gY, halfWidth, halfHeight, Zoom, Color.White, _showPlayerName, false, _showPlayerBar);

            if (ShouldDrawGrid())
            {
                DrawGrid(batcher, srcRect, gX, gY, halfWidth, halfHeight, Zoom);
            }

            if (_showCoordinates)
            {
                var hueVector = new Vector3(0f, 1f, 1f);

                batcher.DrawString(Fonts.Bold, $"{World.Player.X}, {World.Player.Y} ({World.Player.Z}) [{_zoomIndex}]", gX + 6, gY + 6, hueVector);

                hueVector = ShaderHueTranslator.GetHueVector(0);

                batcher.DrawString(Fonts.Bold, $"{World.Player.X}, {World.Player.Y} ({World.Player.Z}) [{_zoomIndex}]", gX + 5, gY + 5, hueVector);
            }

            if (_showMouseCoordinates && _lastMousePosition != null)
            {
                CanvasToWorld(_lastMousePosition.Value.X, _lastMousePosition.Value.Y, out var mouseWorldX, out var mouseWorldY);

                var mouseCoordinateString = $"{mouseWorldX} {mouseWorldY}";

                var size = Fonts.Regular.MeasureString(mouseCoordinateString);

                var mx = gX + 5;
                var my = gY + Height - (int)Math.Ceiling(size.Y) - 15;

                var hueVector = new Vector3(0f, 1f, 1f);

                batcher.DrawString(Fonts.Bold, mouseCoordinateString, mx + 1, my + 1, hueVector);

                hueVector = ShaderHueTranslator.GetHueVector(0);

                batcher.DrawString(Fonts.Bold, mouseCoordinateString, mx, my, hueVector);
            }
        }

        private void DrawMobile(UltimaBatcher2D batcher, Mobile mobile, int x, int y, int width, int height, float zoom, Color color, bool drawName = false, bool isparty = false, bool drawHpBar = false)
        {
            var hueVector = ShaderHueTranslator.GetHueVector(0);

            var sx = mobile.X - _center.X;
            var sy = mobile.Y - _center.Y;

            var rot = RotatePoint(sx, sy, zoom, 1, _flipMap ? 45f : 0f);

            AdjustPosition(rot.X, rot.Y, width - 4, height - 4, out rot.X, out rot.Y);

            rot.X += x + width;
            rot.Y += y + height;

            const int DOT_SIZE = 4;
            const int DOT_SIZE_HALF = DOT_SIZE >> 1;

            if (rot.X < x)
            {
                rot.X = x;
            }

            if (rot.X > x + Width - 8 - DOT_SIZE)
            {
                rot.X = x + Width - 8 - DOT_SIZE;
            }

            if (rot.Y < y)
            {
                rot.Y = y;
            }

            if (rot.Y > y + Height - 8 - DOT_SIZE)
            {
                rot.Y = y + Height - 8 - DOT_SIZE;
            }

            batcher.Draw(SolidColorTextureCache.GetTexture(color), new Rectangle(rot.X - DOT_SIZE_HALF, rot.Y - DOT_SIZE_HALF, DOT_SIZE, DOT_SIZE), hueVector);

            if (drawName && !string.IsNullOrEmpty(mobile.Name))
            {
                var size = Fonts.Regular.MeasureString(mobile.Name);

                if (rot.X + size.X / 2 > x + Width - 8)
                {
                    rot.X = x + Width - 8 - (int)(size.X / 2);
                }
                else if (rot.X - size.X / 2 < x)
                {
                    rot.X = x + (int)(size.X / 2);
                }

                if (rot.Y + size.Y > y + Height)
                {
                    rot.Y = y + Height - (int)size.Y;
                }
                else if (rot.Y - size.Y < y)
                {
                    rot.Y = y + (int)size.Y;
                }

                var xx = (int)(rot.X - size.X / 2);
                var yy = (int)(rot.Y - size.Y);

                hueVector.X = 0;
                hueVector.Y = 1;

                batcher.DrawString(Fonts.Regular, mobile.Name, xx + 1, yy + 1, hueVector);

                hueVector.X = isparty ? 0x0034 : Notoriety.GetHue(mobile.NotorietyFlag);
                hueVector.Y = 1;
                hueVector.Z = 1;

                batcher.DrawString(Fonts.Regular, mobile.Name, xx, yy, hueVector);
            }

            if (drawHpBar)
            {
                int ww = mobile.HitsMax;

                if (ww > 0)
                {
                    ww = mobile.Hits * 100 / ww;

                    if (ww > 100)
                    {
                        ww = 100;
                    }
                    else if (ww < 1)
                    {
                        ww = 0;
                    }
                }

                rot.Y += DOT_SIZE + 1;

                DrawHpBar(batcher, rot.X, rot.Y, ww);
            }
        }

        private bool DrawMarker(UltimaBatcher2D batcher, WMapMarker marker, int x, int y, int width, int height, float zoom)
        {
            if (marker.MapId != World.MapIndex)
            {
                // a negative mapid means "fel or tram"
                if (marker.MapId >= 0 || World.MapIndex > 1)
                {
                    return false;
                }
            }

            if (_zoomIndex < marker.ZoomIndex && marker.Color == Color.Transparent)
            {
                return false;
            }

            var hueVector = ShaderHueTranslator.GetHueVector(0);

            var sx = marker.X - _center.X;
            var sy = marker.Y - _center.Y;

            var rot = RotatePoint(sx, sy, zoom, 1, _flipMap ? 45f : 0f);

            rot.X += x + width;
            rot.Y += y + height;

            const int DOT_SIZE = 4;
            const int DOT_SIZE_HALF = DOT_SIZE >> 1;

            if (rot.X < x || rot.X > x + Width - 8 - DOT_SIZE || rot.Y < y || rot.Y > y + Height - 8 - DOT_SIZE)
            {
                return false;
            }

            var showMarkerName = _showMarkerNames && !string.IsNullOrEmpty(marker.Name) && _zoomIndex > 5;
            var drawSingleName = false;

            if (_zoomIndex < marker.ZoomIndex || !_showMarkerIcons || marker.Icon == null)
            {
                batcher.Draw(SolidColorTextureCache.GetTexture(marker.Color), new Rectangle(rot.X - DOT_SIZE_HALF, rot.Y - DOT_SIZE_HALF, DOT_SIZE, DOT_SIZE), hueVector);

                if (Mouse.Position.X >= rot.X - DOT_SIZE && Mouse.Position.X <= rot.X + DOT_SIZE_HALF
                 && Mouse.Position.Y >= rot.Y - DOT_SIZE && Mouse.Position.Y <= rot.Y + DOT_SIZE_HALF)
                {
                    drawSingleName = true;
                }
            }
            else
            {
                batcher.Draw(marker.Icon, new Vector2(rot.X - (marker.Icon.Width >> 1), rot.Y - (marker.Icon.Height >> 1)), hueVector);

                if (!showMarkerName)
                {
                    if (Mouse.Position.X >= rot.X - (marker.Icon.Width >> 1)
                     && Mouse.Position.X <= rot.X + (marker.Icon.Width >> 1)
                     && Mouse.Position.Y >= rot.Y - (marker.Icon.Height >> 1)
                     && Mouse.Position.Y <= rot.Y + (marker.Icon.Height >> 1))
                    {
                        drawSingleName = true;
                    }
                }
            }

            if (showMarkerName)
            {
                DrawMarkerString(batcher, marker, x, y, width, height);

                drawSingleName = false;
            }

            return drawSingleName;
        }

        private void DrawMarkerString(UltimaBatcher2D batcher, WMapMarker marker, int x, int y, int width, int height)
        {
            var sx = marker.X - _center.X;
            var sy = marker.Y - _center.Y;

            var rot = RotatePoint(sx, sy, Zoom, 1, _flipMap ? 45f : 0f);

            rot.X += x + width;
            rot.Y += y + height;

            var size = _markerFont.MeasureString(marker.Name);

            if (rot.X + size.X / 2 > x + Width - 8)
            {
                rot.X = x + Width - 8 - (int)(size.X / 2);
            }
            else if (rot.X - size.X / 2 < x)
            {
                rot.X = x + (int)(size.X / 2);
            }

            if (rot.Y + size.Y > y + Height)
            {
                rot.Y = y + Height - (int)size.Y;
            }
            else if (rot.Y - size.Y < y)
            {
                rot.Y = y + (int)size.Y;
            }

            var xx = (int)(rot.X - size.X / 2);
            var yy = (int)(rot.Y - size.Y - 5);

            var hueVector = new Vector3(0f, 1f, 0.5f);

            batcher.Draw(SolidColorTextureCache.GetTexture(Color.Black), new Rectangle(xx - 2, yy - 2, (int)(size.X + 4), (int)(size.Y + 4)), hueVector);

            hueVector = new Vector3(0f, 1f, 1f);

            batcher.DrawString(_markerFont, marker.Name, xx + 1, yy + 1, hueVector);

            hueVector = ShaderHueTranslator.GetHueVector(0);

            batcher.DrawString(_markerFont, marker.Name, xx, yy, hueVector);
        }

        private void DrawMulti(UltimaBatcher2D batcher, House house, int multiX, int multiY, int x, int y, int width, int height, float zoom)
        {
            var sx = multiX - _center.X;
            var sy = multiY - _center.Y;
            var sW = Math.Abs(house.Bounds.Width - house.Bounds.X);
            var sH = Math.Abs(house.Bounds.Height - house.Bounds.Y);

            var rot = RotatePoint(sx, sy, zoom, 1, _flipMap ? 45f : 0f);

            rot.X += x + width;
            rot.Y += y + height;

            const int DOT_SIZE = 4;

            if (rot.X < x || rot.X > x + Width - 8 - DOT_SIZE || rot.Y < y || rot.Y > y + Height - 8 - DOT_SIZE)
            {
                return;
            }

            var hueVector = ShaderHueTranslator.GetHueVector(0);

            var texture = SolidColorTextureCache.GetTexture(Color.DarkGray);

            batcher.Draw(texture, new Rectangle(rot.X, rot.Y, (int)(sW * zoom), (int)(sH * zoom)), null, hueVector, _flipMap ? Microsoft.Xna.Framework.MathHelper.ToRadians(45) : 0, new Vector2(0.5f, 0.5f), SpriteEffects.None, 0);
        }

        private Vector2 WorldPointToGumpPoint(int wpx, int wpy, int x, int y, int width, int height, float zoom)
        {
            int sx = wpx - _center.X;
            int sy = wpy - _center.Y;

            Point rot = RotatePoint(sx, sy, zoom, 1, _flipMap ? 45f : 0f);

            /* N.B. You don't want AdjustPosition() here if you want to draw rects
             * that extend beyond the gump's viewport without distoring them. */

            rot.X += x + width;
            rot.Y += y + height;

            return new Vector2(rot.X, rot.Y);
        }

        private void DrawZone(UltimaBatcher2D batcher, Zone zone, int x, int y, int width, int height, float zoom)
        {
            Vector3 hueVector = ShaderHueTranslator.GetHueVector(0);
            Texture2D texture = SolidColorTextureCache.GetTexture(zone.Color);

            for (int i = 0, j = 1; i < zone.Vertices.Count; i++, j++)
            {
                if (j >= zone.Vertices.Count)
                {
                    j = 0;
                }

                var start = WorldPointToGumpPoint(zone.Vertices[i].X, zone.Vertices[i].Y, x, y, width, height, zoom);
                var end = WorldPointToGumpPoint(zone.Vertices[j].X, zone.Vertices[j].Y, x, y, width, height, zoom);

                batcher.DrawLine(texture, start, end, hueVector, 1);
            }
        }

        private void DrawGrid(UltimaBatcher2D batcher, Rectangle srcRect, int x, int y, int width, int height, float zoom)
        {
            const int GRID_SKIP = 8;

            var hueVector = ShaderHueTranslator.GetHueVector(0);
            var colorTexture = SolidColorTextureCache.GetTexture(_semiTransparentWhiteForGrid);

            batcher.SetBlendState(BlendState.Additive);

            for (var worldY = (srcRect.Y / GRID_SKIP) * GRID_SKIP; worldY < srcRect.Y + srcRect.Height; worldY += GRID_SKIP)
            {
                var start = WorldPointToGumpPoint(srcRect.X, worldY, x, y, width, height, zoom);
                var end = WorldPointToGumpPoint(srcRect.X + srcRect.Width, worldY, x, y, width, height, zoom);

                batcher.DrawLine(colorTexture, start, end, hueVector, 1);
            }

            for (var worldX = (srcRect.X / GRID_SKIP) * GRID_SKIP; worldX < srcRect.X + srcRect.Width; worldX += GRID_SKIP)
            {
                var start = WorldPointToGumpPoint(worldX, srcRect.Y, x, y, width, height, zoom);
                var end = WorldPointToGumpPoint(worldX, srcRect.Y + srcRect.Height, x, y, width, height, zoom);

                batcher.DrawLine(colorTexture, start, end, hueVector, 1);
            }

            batcher.SetBlendState(null);
        }

        private void DrawWMEntity(UltimaBatcher2D batcher, WMapEntity entity, int x, int y, int width, int height, float zoom)
        {
            var hueVector = ShaderHueTranslator.GetHueVector(0);

            ushort uohue;
            Color color;

            if (entity.IsGuild)
            {
                uohue = 0x0044;
                color = Color.LimeGreen;
            }
            else
            {
                uohue = 0x0034;
                color = Color.Yellow;
            }

            if (entity.Map != World.MapIndex)
            {
                uohue = 992;
                color = Color.DarkGray;
            }

            var sx = entity.X - _center.X;
            var sy = entity.Y - _center.Y;

            var rot = RotatePoint(sx, sy, zoom, 1, _flipMap ? 45f : 0f);

            AdjustPosition(rot.X, rot.Y, width - 4, height - 4, out rot.X, out rot.Y);

            rot.X += x + width;
            rot.Y += y + height;

            const int DOT_SIZE = 4;
            const int DOT_SIZE_HALF = DOT_SIZE >> 1;

            if (rot.X < x)
            {
                rot.X = x;
            }

            if (rot.X > x + Width - 8 - DOT_SIZE)
            {
                rot.X = x + Width - 8 - DOT_SIZE;
            }

            if (rot.Y < y)
            {
                rot.Y = y;
            }

            if (rot.Y > y + Height - 8 - DOT_SIZE)
            {
                rot.Y = y + Height - 8 - DOT_SIZE;
            }

            batcher.Draw(SolidColorTextureCache.GetTexture(color), new Rectangle(rot.X - DOT_SIZE_HALF, rot.Y - DOT_SIZE_HALF, DOT_SIZE, DOT_SIZE), hueVector);

            if (_showGroupName)
            {
                var name = entity.Name ?? ResGumps.OutOfRange;
                var size = Fonts.Regular.MeasureString(entity.Name ?? name);

                if (rot.X + size.X / 2 > x + Width - 8)
                {
                    rot.X = x + Width - 8 - (int)(size.X / 2);
                }
                else if (rot.X - size.X / 2 < x)
                {
                    rot.X = x + (int)(size.X / 2);
                }

                if (rot.Y + size.Y > y + Height)
                {
                    rot.Y = y + Height - (int)size.Y;
                }
                else if (rot.Y - size.Y < y)
                {
                    rot.Y = y + (int)size.Y;
                }

                var xx = (int)(rot.X - size.X / 2);
                var yy = (int)(rot.Y - size.Y);

                hueVector.X = 0;
                hueVector.Y = 1;

                batcher.DrawString(Fonts.Regular, name, xx + 1, yy + 1, hueVector);

                hueVector = new Vector3(uohue, 1f, 1f);

                batcher.DrawString(Fonts.Regular, name, xx, yy, hueVector);
            }

            if (_showGroupBar)
            {
                rot.Y += DOT_SIZE + 1;

                DrawHpBar(batcher, rot.X, rot.Y, entity.HP);
            }
        }

        private void DrawHpBar(UltimaBatcher2D batcher, int x, int y, int hp)
        {
            const int BAR_MAX_WIDTH = 25;
            const int BAR_MAX_WIDTH_HALF = BAR_MAX_WIDTH / 2;

            const int BAR_MAX_HEIGHT = 3;
            const int BAR_MAX_HEIGHT_HALF = BAR_MAX_HEIGHT / 2;

            var hueVector = ShaderHueTranslator.GetHueVector(0);

            batcher.Draw(SolidColorTextureCache.GetTexture(Color.Black), new Rectangle(x - BAR_MAX_WIDTH_HALF - 1, y - BAR_MAX_HEIGHT_HALF - 1, BAR_MAX_WIDTH + 2, BAR_MAX_HEIGHT + 2), hueVector);

            batcher.Draw(SolidColorTextureCache.GetTexture(Color.Red), new Rectangle(x - BAR_MAX_WIDTH_HALF, y - BAR_MAX_HEIGHT_HALF, BAR_MAX_WIDTH, BAR_MAX_HEIGHT), hueVector);

            int max = 100;
            int current = hp;

            if (max > 0)
            {
                max = current * 100 / max;

                if (max > 100)
                {
                    max = 100;
                }

                if (max > 1)
                {
                    max = BAR_MAX_WIDTH * max / 100;
                }
            }

            batcher.Draw(SolidColorTextureCache.GetTexture(Color.CornflowerBlue), new Rectangle(x - BAR_MAX_WIDTH_HALF, y - BAR_MAX_HEIGHT_HALF, max, BAR_MAX_HEIGHT), hueVector);
        }

        #endregion

        #region I/O

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            var allowTarget = _allowPositionalTarget && TargetManager.IsTargeting && TargetManager.TargetingState == CursorTarget.Position;

            if (allowTarget && button == MouseButtonType.Left)
            {
                HandlePositionTarget();
            }

            if (button == MouseButtonType.Left && !Keyboard.Alt)
            {
                _isScrolling = false;
                CanMove = true;
            }

            if (button == MouseButtonType.Left || button == MouseButtonType.Middle)
            {
                _lastScroll.X = _center.X;
                _lastScroll.Y = _center.Y;
            }

            Client.Game.GameCursor.IsDraggingCursorForced = false;

            base.OnMouseUp(x, y, button);
        }

        protected override void OnMouseDown(int x, int y, MouseButtonType button)
        {
            if (!Client.Game.GameCursor.ItemHold.Enabled)
            {
                if (button == MouseButtonType.Left && (Keyboard.Alt || _freeView) || button == MouseButtonType.Middle)
                {
                    if (x > 4 && x < Width - 8 && y > 4 && y < Height - 8)
                    {
                        if (button == MouseButtonType.Middle)
                        {
                            FreeView = true;
                        }

                        _lastScroll.X = _center.X;
                        _lastScroll.Y = _center.Y;

                        _isScrolling = true;
                        CanMove = false;

                        Client.Game.GameCursor.IsDraggingCursorForced = true;
                    }
                }

                if (button == MouseButtonType.Left && Keyboard.Ctrl)
                {
                    CanvasToWorld(x, y, out _mouseCenter.X, out _mouseCenter.Y);

                    // Check if file is loaded and contain markers
                    var userFile = UserMarkersFile;

                    if (userFile == null)
                    {
                        return;
                    }

                    var existingGump = UIManager.GetGump<UserMarkerGump>();

                    existingGump?.Dispose();

                    UIManager.Add(new UserMarkerGump(null)
                    {
                        InputX = _mouseCenter.X,
                        InputY = _mouseCenter.Y,
                    });
                }
            }

            base.OnMouseDown(x, y, button);
        }

        protected override void OnMouseOver(int x, int y)
        {
            _lastMousePosition = new Point(x, y);

            var offset = Mouse.LButtonPressed ? Mouse.LDragOffset : Mouse.MButtonPressed ? Mouse.MDragOffset : Point.Zero;

            if (_isScrolling && offset != Point.Zero)
            {
                _scroll.X = _scroll.Y = 0;

                if (Mouse.LButtonPressed)
                {
                    _scroll.X = x - (Mouse.LClickPosition.X - X);
                    _scroll.Y = y - (Mouse.LClickPosition.Y - Y);
                }
                else if (Mouse.MButtonPressed)
                {
                    _scroll.X = x - (Mouse.MClickPosition.X - X);
                    _scroll.Y = y - (Mouse.MClickPosition.Y - Y);
                }

                if (_scroll == Point.Zero)
                {
                    return;
                }

                _scroll = RotatePoint
                (
                    _scroll.X,
                    _scroll.Y,
                    1f / Zoom,
                    -1,
                    _flipMap ? 45f : 0f
                );

                _center.X = _lastScroll.X - _scroll.X;
                _center.Y = _lastScroll.Y - _scroll.Y;

                if (_center.X < 0)
                {
                    _center.X = 0;
                }

                if (_center.Y < 0)
                {
                    _center.Y = 0;
                }

                if (_center.X > MapLoader.Instance.MapsDefaultSize[World.MapIndex, 0])
                {
                    _center.X = MapLoader.Instance.MapsDefaultSize[World.MapIndex, 0];
                }

                if (_center.Y > MapLoader.Instance.MapsDefaultSize[World.MapIndex, 1])
                {
                    _center.Y = MapLoader.Instance.MapsDefaultSize[World.MapIndex, 1];
                }
            }
            else
            {
                base.OnMouseOver(x, y);
            }
        }

        protected override void OnMouseWheel(MouseEventType delta)
        {
            if (delta == MouseEventType.WheelScrollUp)
            {
                _zoomIndex++;

                if (_zoomIndex >= _zooms.Length)
                {
                    _zoomIndex = _zooms.Length - 1;
                }
            }
            else
            {
                _zoomIndex--;

                if (_zoomIndex < 0)
                {
                    _zoomIndex = 0;
                }
            }

            base.OnMouseWheel(delta);
        }

        protected override bool OnMouseDoubleClick(int x, int y, MouseButtonType button)
        {
            if (button != MouseButtonType.Left || _isScrolling || Keyboard.Alt)
            {
                return base.OnMouseDoubleClick(x, y, button);
            }

            TopMost = !TopMost;

            return true;
        }

        protected override void OnMouseExit(int x, int y)
        {
            _lastMousePosition = null;

            base.OnMouseExit(x, y);
        }

        protected override void OnMove(int x, int y)
        {
            base.OnMove(x, y);

            _last_position.X = ScreenCoordinateX;
            _last_position.Y = ScreenCoordinateY;
        }

        #endregion
    }
}