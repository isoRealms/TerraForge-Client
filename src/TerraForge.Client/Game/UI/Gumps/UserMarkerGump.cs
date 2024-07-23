using System;
using System.IO;
using System.Linq;

using ClassicUO.Assets;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Resources;
using ClassicUO.Utility;

using static ClassicUO.Game.UI.Gumps.WorldMapGump;

namespace ClassicUO.Game.UI.Gumps
{
    internal sealed class UserMarkerGump : Gump
    {
        private enum ButtonsOption
        {
            ADD_BTN,
            EDIT_BTN,
            CANCEL_BTN,
        }

        private const ushort HUE_FONT = 0xFFFF;
        private const ushort LABEL_OFFSET = 40;
        private const ushort Y_OFFSET = 30;

        private const int MAX_CORD_LEN = 10;
        private const int MAX_NAME_LEN = 100;

        private readonly AlphaBlendControl _background;

        private readonly StbTextBox _textBoxX;
        private readonly Label _textBoxXLabel;

        private readonly StbTextBox _textBoxY;
        private readonly Label _textBoxYLabel;

        private readonly StbTextBox _markerName;
        private readonly Label _markerNameLabel;

        private readonly Combobox _colorsCombo;
        private readonly Combobox _iconsCombo;

        private readonly NiceButton _addOrEditButton;

        private readonly WMapMarker _marker;

        private readonly int _mapId;

        public bool IsAdd => _marker == null;
        public bool IsEdit => _marker != null;

        public int InputXMax => MapLoader.Instance.MapsDefaultSize[_mapId, 0];
        public int InputYMax => MapLoader.Instance.MapsDefaultSize[_mapId, 1];

        public int InputX
        {
            get
            {
                if (int.TryParse(_textBoxX?.Text, out var x))
                {
                    return x;
                }

                return -1;
            }
            set => _textBoxX.Text = $"{Math.Max(0, Math.Min(InputXMax, value))}";
        }

        public int InputY
        {
            get
            {
                if (int.TryParse(_textBoxY?.Text, out var y))
                {
                    return y;
                }

                return -1;
            }
            set => _textBoxY.Text = $"{Math.Max(0, Math.Min(InputYMax, value))}";
        }

        public string InputName
        {
            get => _markerName.Text?.Replace(",", "_") ?? string.Empty;
            set => _markerName.Text = value?.Replace(",", "_") ?? string.Empty;
        }

        public event EventHandler<WMapMarker> OnMarkerEdit, OnMarkerAdd;

        internal UserMarkerGump(WMapMarker marker) : base(0, 0)
        {
            CanMove = true;

            _marker = marker;

            _mapId = _marker?.MapId ?? World.MapIndex;

            Add(_background = new AlphaBlendControl
            {
                Width = 320,
                Height = 220,
                X = Client.Game.Scene.Camera.Bounds.Width / 2 - 125,
                Y = 150,
                Alpha = 0.7f,
                CanMove = true,
                CanCloseWithRightClick = true,
                AcceptMouseInput = true
            });

            Add(new Label(IsAdd ? ResGumps.AddMarker : ResGumps.EditMarker, true, HUE_FONT, 0, 255, FontStyle.BlackBorder)
            {
                X = _background.X + 100,
                Y = _background.Y + 3,
            });

            // X Field
            var fx = _background.X + 5;
            var fy = _background.Y + 25;

            Add(new ResizePic(0x0BB8)
            {
                X = fx + LABEL_OFFSET,
                Y = fy,
                Width = 90,
                Height = 25
            });

            Add(_textBoxX = new StbTextBox(0xFF, MAX_CORD_LEN, 90, true, FontStyle.BlackBorder | FontStyle.Fixed)
            {
                X = fx + LABEL_OFFSET,
                Y = fy,
                Width = 90,
                Height = 25,
                Text = $"{_marker?.X ?? 0}"
            });

            Add(new Label(ResGumps.MarkerX, true, HUE_FONT, 0, 255, FontStyle.BlackBorder)
            {
                X = fx,
                Y = fy
            });

            // Y Field
            fy += Y_OFFSET;

            Add(new ResizePic(0x0BB8)
            {
                X = fx + LABEL_OFFSET,
                Y = fy,
                Width = 90,
                Height = 25
            });

            Add(_textBoxY = new StbTextBox(0xFF, MAX_CORD_LEN, 90, true, FontStyle.BlackBorder | FontStyle.Fixed)
            {
                X = fx + LABEL_OFFSET,
                Y = fy,
                Width = 90,
                Height = 25,
                Text = $"{_marker?.Y ?? 0}"
            });

            Add(new Label(ResGumps.MarkerY, true, HUE_FONT, 0, 255, FontStyle.BlackBorder)
            {
                X = fx,
                Y = fy
            });

            // Marker Name field
            fy += Y_OFFSET;

            Add(new ResizePic(0x0BB8)
            {
                X = fx + LABEL_OFFSET,
                Y = fy,
                Width = 250,
                Height = 25
            });

            Add(_markerName = new StbTextBox(0xFF, MAX_NAME_LEN, 250, true, FontStyle.BlackBorder | FontStyle.Fixed)
            {
                X = fx + LABEL_OFFSET,
                Y = fy,
                Width = 250,
                Height = 25,
                Text = _marker?.Name ?? ResGumps.MarkerDefName
            });

            Add(new Label(ResGumps.MarkerName, true, HUE_FONT, 0, 255, FontStyle.BlackBorder)
            {
                X = fx,
                Y = fy
            });

            if (MarkerColors.Count > 0)
            {
                // Color Combobox
                fy += Y_OFFSET;

                Add(_colorsCombo = new Combobox(fx + LABEL_OFFSET, fy, 250, [.. MarkerColors.Keys]));

                Add(new Label(ResGumps.MarkerColor, true, HUE_FONT, 0, 255, FontStyle.BlackBorder)
                {
                    X = fx,
                    Y = fy
                });
            }

            if (MarkerIcons.Count > 0)
            {
                // Icon combobox
                fy += Y_OFFSET;

                Add(_iconsCombo = new Combobox(fx + LABEL_OFFSET, fy, 250, [.. MarkerIcons.Keys]));

                Add(new Label(ResGumps.MarkerIcon, true, HUE_FONT, 0, 255, FontStyle.BlackBorder)
                {
                    X = fx,
                    Y = fy
                });
            }

            var bx = _background.X + 13;
            var by = _background.Y + _background.Height - 30;

            Add(_addOrEditButton = new NiceButton(bx, by, 60, 25, ButtonAction.Activate, IsAdd ? ResGumps.CreateMarker : ResGumps.Edit)
            {
                ButtonParameter = (int)(IsAdd ? ButtonsOption.ADD_BTN : ButtonsOption.EDIT_BTN),
                IsSelectable = false
            });

            bx += _addOrEditButton.Width + 5;

            Add(new NiceButton(bx, by, 60, 25, ButtonAction.Activate, ResGumps.Cancel)
            {
                ButtonParameter = (int)ButtonsOption.CANCEL_BTN,
                IsSelectable = false
            });

            SetInScreen();
        }

        private void EditMarker()
        {
            var marker = PrepareMarker();

            if (marker != null)
            {
                OnMarkerEdit.Raise(marker, this);

                Dispose();
            }
        }

        private void AddMarker()
        {
            var marker = PrepareMarker();

            if (marker != null)
            {
                OnMarkerAdd.Raise(marker, this);

                Dispose();
            }
        }

        private WMapMarker PrepareMarker()
        {
            if (!ValidateInput())
            {
                return null;
            }

            var color = _colorsCombo.SelectedItem ?? "white";
            var icon = _iconsCombo.SelectedItem ?? string.Empty;

            var marker = new WMapMarker
            {
                X = InputX,
                Y = InputY,
                Name = InputName,
                MapId = _mapId,
                IconName = icon,
                ColorName = color,
            };

            return marker;
        }

        private bool ValidateInput()
        {
            var valid = true;

            var x = InputX;

            if (x < 0 || x > InputXMax)
            {
                valid = false;

                _textBoxXLabel.Hue = 0x22;
            }
            else
            {
                _textBoxXLabel.Hue = 0;
            }

            var y = InputY;

            if (y < 0 || y > InputYMax)
            {
                valid = false;

                _textBoxYLabel.Hue = 0x22;
            }
            else
            {
                _textBoxYLabel.Hue = 0;
            }

            var name = InputName;

            if (string.IsNullOrWhiteSpace(name))
            {
                valid = false;

                _markerNameLabel.Hue = 0x22;
            }
            else
            {
                _markerNameLabel.Hue = 0;
            }

            return valid;
        }

        public override void OnButtonClick(int buttonId)
        {
            switch (buttonId)
            {
                case (int)ButtonsOption.ADD_BTN:
                {
                    AddMarker();
                    break;
                }

                case (int)ButtonsOption.EDIT_BTN:
                {
                    EditMarker();
                    break;
                }

                case (int)ButtonsOption.CANCEL_BTN:
                {
                    Dispose();
                    break;
                }
            }
        }
    }
}