﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Media;
using Windows.Media.Devices;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using LibVLCSharp.Platforms.UWP;
using LibVLCSharp.Shared;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Uwp.UI;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Converters;
using Screenbox.Core;
using Screenbox.Services;

namespace Screenbox.ViewModels
{
    internal partial class PlayerViewModel : ObservableObject, IDisposable
    {
        public ICommand PlayPauseCommand { get; }
        public ICommand FullscreenCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand ToggleCompactLayoutCommand { get; }

        public MediaPlayer MediaPlayer
        {
            get => _mediaPlayer;
            private set => SetProperty(ref _mediaPlayer, value);
        }

        public string MediaTitle
        {
            get => _mediaTitle;
            set => SetProperty(ref _mediaTitle, value);
        }

        public bool IsFullscreen
        {
            get => _isFullscreen;
            private set => SetProperty(ref _isFullscreen, value);
        }

        public bool IsCompact
        {
            get => _isCompact;
            private set => SetProperty(ref _isCompact, value);
        }

        public bool BufferingVisible
        {
            get => _bufferingVisible;
            private set => SetProperty(ref _bufferingVisible, value);
        }

        public bool ControlsHidden
        {
            get => _controlsHidden;
            private set => SetProperty(ref _controlsHidden, value);
        }

        public bool ZoomToFit
        {
            get => _zoomToFit;
            set => SetProperty(ref _zoomToFit, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public NotificationRaisedEventArgs Notification
        {
            get => _notification;
            private set => SetProperty(ref _notification, value);
        }

        public Size ViewSize
        {
            get => _viewSize;
            private set => SetProperty(ref _viewSize, value);
        }

        public bool VideoViewFocused
        {
            get => _videoViewFocused;
            set => SetProperty(ref _videoViewFocused, value);
        }

        public bool PlayerHidden
        {
            get => _playerHidden;
            set => SetProperty(ref _playerHidden, value);
        }

        public object ToBeOpened { get; set; }

        private readonly DispatcherQueue _dispatcherQueue;
        private readonly DispatcherQueueTimer _controlsVisibilityTimer;
        private readonly DispatcherQueueTimer _statusMessageTimer;
        private readonly DispatcherQueueTimer _bufferingTimer;
        private readonly SystemMediaTransportControls _transportControl;
        private readonly IFilesService _filesService;
        private readonly INotificationService _notificationService;
        private readonly IPlaylistService _playlistService;
        private LibVLC _libVlc;
        private Media _media;
        private string _mediaTitle;
        private MediaPlayer _mediaPlayer;
        private Size _viewSize;
        private bool _isFullscreen;
        private bool _controlsHidden;
        private CoreCursor _cursor;
        private bool _visibilityOverride;
        private bool _isCompact;
        private string _statusMessage;
        private bool _zoomToFit;
        private bool _bufferingVisible;
        private NotificationRaisedEventArgs _notification;
        private Stream _fileStream;
        private StreamMediaInput _streamMediaInput;
        private bool _videoViewFocused;
        private bool _playerHidden;

        public PlayerViewModel(
            IFilesService filesService,
            INotificationService notificationService,
            IPlaylistService playlistService)
        {
            _filesService = filesService;
            _notificationService = notificationService;
            _playlistService = playlistService;
            _playlistService.OpenRequested += OnOpenRequested;
            _notificationService.NotificationRaised += OnNotificationRaised;
            _notificationService.ProgressUpdated += OnProgressUpdated;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _transportControl = SystemMediaTransportControls.GetForCurrentView();
            _controlsVisibilityTimer = _dispatcherQueue.CreateTimer();
            _statusMessageTimer = _dispatcherQueue.CreateTimer();
            _bufferingTimer = _dispatcherQueue.CreateTimer();

            PlayPauseCommand = new RelayCommand(PlayPause);
            FullscreenCommand = new RelayCommand<bool>(SetFullscreen);
            OpenCommand = new RelayCommand<object>(Open);
            ToggleCompactLayoutCommand = new RelayCommand(ToggleCompactLayout);

            MediaDevice.DefaultAudioRenderDeviceChanged += MediaDevice_DefaultAudioRenderDeviceChanged;
            _transportControl.ButtonPressed += TransportControl_ButtonPressed;
            InitSystemTransportControls();
            PropertyChanged += OnPropertyChanged;

            ShouldUpdateTime = true;
            _bufferingProgress = 100;
            _volume = 100;
            _state = VLCState.NothingSpecial;
        }

        private void OnOpenRequested(object sender, object e)
        {
            _dispatcherQueue.TryEnqueue(() => Open(e));
        }

        private void OnProgressUpdated(object sender, ProgressUpdatedEventArgs e)
        {
            LogService.Log(e.Title);
            LogService.Log(e.Text);
            LogService.Log(e.Value);
        }

        private void OnNotificationRaised(object sender, NotificationRaisedEventArgs e)
        {
            _dispatcherQueue.TryEnqueue(() => Notification = e);
        }

        private void ChangeVolume(double changeAmount)
        {
            Volume += changeAmount;
            ShowStatusMessage($"Volume {Volume:F0}%");
        }

        private async void ToggleCompactLayout()
        {
            var view = ApplicationView.GetForCurrentView();
            if (IsCompact)
            {
                if (await view.TryEnterViewModeAsync(ApplicationViewMode.Default))
                {
                    IsCompact = false;
                }
            }
            else
            {
                var preferences = ViewModePreferences.CreateDefault(ApplicationViewMode.CompactOverlay);
                preferences.ViewSizePreference = ViewSizePreference.Custom;
                preferences.CustomSize = new Size(240 * (NumericAspectRatio ?? 1), 240);
                if (await view.TryEnterViewModeAsync(ApplicationViewMode.CompactOverlay, preferences))
                {
                    IsCompact = true;
                }
            }
        }

        private async void Open(object value)
        {
            PlayerHidden = false;
            if (MediaPlayer == null)
            {
                ToBeOpened = value;
                return;
            }

            Media media = null;
            Uri uri = null;
            Stream stream = null;
            StreamMediaInput streamInput = null;

            if (value is StorageFile file)
            {
                uri = new Uri(file.Path);
                if (!file.IsAvailable) return;
                stream = await file.OpenStreamForReadAsync();
                streamInput = new StreamMediaInput(stream);
                media = new Media(_libVlc, streamInput);
            }

            if (value is string str)
            {
                Uri.TryCreate(str, UriKind.Absolute, out uri);
                value = uri;
            }

            if (value is Uri)
            {
                uri = (Uri)value;
                media = new Media(_libVlc, uri);
            }

            if (media == null)
            {
                return;
            }

            if (uri.Segments.Length > 0)
            {
                MediaTitle = Uri.UnescapeDataString(uri.Segments.Last());
            }

            var oldMedia = _media;
            var oldStream = _fileStream;
            var oldStreamInput = _streamMediaInput;
            _media = media;
            _fileStream = stream;
            _streamMediaInput = streamInput;

            media.ParsedChanged += OnMediaParsed;
            MediaPlayer.Play(media);

            oldMedia?.Dispose();
            oldStream?.Dispose();
            oldStreamInput?.Dispose();
        }

        private void OnMediaParsed(object sender, MediaParsedChangedEventArgs e)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                SpuDescriptions = MediaPlayer.SpuDescription;
                SpuIndex = GetIndexFromTrackId(MediaPlayer.Spu, MediaPlayer.SpuDescription);
                AudioTrackDescriptions = MediaPlayer.AudioTrackDescription;
                AudioTrackIndex = GetIndexFromTrackId(MediaPlayer.AudioTrack, MediaPlayer.AudioTrackDescription);

                if (SetWindowSize(1)) return;
                SetWindowSize();
            });
        }

        private void SetPlaybackSpeed(float speed)
        {
            if (speed != MediaPlayer.Rate)
            {
                MediaPlayer.SetRate(speed);
            }
        }

        private void MediaDevice_DefaultAudioRenderDeviceChanged(object sender, DefaultAudioRenderDeviceChangedEventArgs args)
        {
            if (args.Role == AudioDeviceRole.Default)
            {
                MediaPlayer.SetOutputDevice(MediaPlayer.OutputDevice);
            }
        }

        private void SetFullscreen(bool value)
        {
            var view = ApplicationView.GetForCurrentView();
            if (view.IsFullScreenMode && !value)
            {
                view.ExitFullScreenMode();
            }

            if (!view.IsFullScreenMode && value)
            {
                view.TryEnterFullScreenMode();
            }

            IsFullscreen = view.IsFullScreenMode;
        }

        public void ToggleFullscreen() => SetFullscreen(!IsFullscreen);

        public void OnInitialized(object sender, InitializedEventArgs e)
        {
            _libVlc = App.DerivedCurrent.InitializeLibVlc(e.SwapChainOptions);
            InitMediaPlayer(_libVlc);
            RegisterMediaPlayerPlaybackEvents();
            
            Open(ToBeOpened);
        }

        public void OnBackRequested()
        {
            PlayerHidden = true;
            if (MediaPlayer?.IsPlaying ?? false)
            {
                MediaPlayer.Pause();
            }
        }

        private void ShowStatusMessage(string message)
        {
            StatusMessage = message;
            _statusMessageTimer.Debounce(() => StatusMessage = null, TimeSpan.FromSeconds(1));
        }

        private void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerState):
                    if (ControlsHidden && PlayerState != VLCState.Playing)
                    {
                        ShowControls();
                    }

                    if (!ControlsHidden && PlayerState == VLCState.Playing)
                    {
                        DelayHideControls();
                    }

                    break;

                case nameof(BufferingProgress):
                    _bufferingTimer.Debounce(
                        () => BufferingVisible = BufferingProgress < 100,
                        TimeSpan.FromSeconds(0.5));
                    break;

                case nameof(ZoomToFit):
                    OnSizeChanged(null, null);
                    break;

                case nameof(VideoViewFocused):
                    if (VideoViewFocused)
                    {
                        DelayHideControls();
                    }
                    else
                    {
                        ShowControls();
                    }
                    break;
            }
        }

        public void Dispose()
        {
            MediaDevice.DefaultAudioRenderDeviceChanged -= MediaDevice_DefaultAudioRenderDeviceChanged;
            _transportControl.ButtonPressed -= TransportControl_ButtonPressed;
            _controlsVisibilityTimer.Stop();
            _bufferingTimer.Stop();
            _statusMessageTimer.Stop();

            if (MediaPlayer != null)
            {
                RemoveMediaPlayerEventHandlers();
                MediaPlayer.Dispose();
            }
            _media?.Dispose();
            _streamMediaInput?.Dispose();
            _fileStream?.Dispose();
        }

        private void PlayPause()
        {
            if (MediaPlayer.IsPlaying && MediaPlayer.CanPause)
            {
                MediaPlayer.Pause();
            }

            if (!MediaPlayer.IsPlaying && MediaPlayer.WillPlay)
            {
                MediaPlayer.Play();
            }

            if (MediaPlayer.State == VLCState.Ended)
            {
                Replay();
            }
        }

        private void Seek(long amount)
        {
            if (MediaPlayer.IsSeekable)
            {
                MediaPlayer.Time += amount;
                ShowStatusMessage($"{HumanizedDurationConverter.Convert(MediaPlayer.Time)} / {HumanizedDurationConverter.Convert(MediaPlayer.Length)}");
            }
        }

        public bool JumpFrame(bool previous = false)
        {
            if (MediaPlayer.State == VLCState.Paused && MediaPlayer.IsSeekable)
            {
                if (previous)
                {
                    MediaPlayer.Time -= FrameDuration;
                }
                else
                {
                    MediaPlayer.NextFrame();
                }

                return true;
            }

            return false;
        }

        public void ToggleControlsVisibility()
        {
            if (ControlsHidden)
            {
                ShowControls();
                DelayHideControls();
            }
            else if (MediaPlayer.IsPlaying && !_visibilityOverride)
            {
                HideControls();
                // Keep hiding even when pointer moved right after
                OverrideVisibilityChange();
            }
        }

        public void OnPlaybackSpeedItemClick(object sender, RoutedEventArgs e)
        {
            var item = (RadioMenuFlyoutItem)sender;
            var speedText = item.Text;
            float.TryParse(speedText, out var speed);
            SetPlaybackSpeed(speed);
        }

        private bool SetWindowSize(double scalar = 0)
        {
            if (scalar < 0) return false;
            var displayInformation = DisplayInformation.GetForCurrentView();
            var view = ApplicationView.GetForCurrentView();
            var maxWidth = displayInformation.ScreenWidthInRawPixels / displayInformation.RawPixelsPerViewPixel;
            var maxHeight = displayInformation.ScreenHeightInRawPixels / displayInformation.RawPixelsPerViewPixel - 48;
            if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
            {
                var displayRegion = view.GetDisplayRegions()[0];
                maxWidth = displayRegion.WorkAreaSize.Width / displayInformation.RawPixelsPerViewPixel;
                maxHeight = displayRegion.WorkAreaSize.Height / displayInformation.RawPixelsPerViewPixel;
            }

            maxHeight -= 16;
            maxWidth -= 16;

            var videoDimension = Dimension;
            if (!videoDimension.IsEmpty)
            {
                if (scalar == 0)
                {
                    var widthRatio = maxWidth / videoDimension.Width;
                    var heightRatio = maxHeight / videoDimension.Height;
                    scalar = Math.Min(widthRatio, heightRatio);
                }

                var aspectRatio = videoDimension.Width / videoDimension.Height;
                var newWidth = videoDimension.Width * scalar;
                if (newWidth > maxWidth) newWidth = maxWidth;
                var newHeight = newWidth / aspectRatio;
                scalar = newWidth / videoDimension.Width;
                if (view.TryResizeView(new Size(newWidth, newHeight)))
                {
                    ShowStatusMessage($"Scale {scalar * 100:0.##}%");
                    return true;
                }
            }

            return false;
        }

        public void OnSizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (args != null) ViewSize = args.NewSize;
            if (MediaPlayer == null) return;
            if (!ZoomToFit && MediaPlayer.CropGeometry == null) return;
            MediaPlayer.CropGeometry = ZoomToFit ? $"{ViewSize.Width}:{ViewSize.Height}" : null;
        }

        public void OnPointerMoved()
        {
            if (!_visibilityOverride)
            {
                if (ControlsHidden)
                {
                    ShowControls();
                }

                if (!ShouldUpdateTime) return;
                DelayHideControls();
            }
        }

        public void OnDragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = "Open";
        }

        public async void OnDrop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0)
                {
                    Open(items[0]);
                }
            }

            if (e.DataView.Contains(StandardDataFormats.WebLink))
            {
                var uri = await e.DataView.GetWebLinkAsync();
                if (uri.IsFile)
                {
                    Open(uri);
                }
            }
        }

        public void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var pointer = e.GetCurrentPoint((UIElement)e.OriginalSource);
            var mouseWheelDelta = pointer.Properties.MouseWheelDelta;
            ChangeVolume(mouseWheelDelta / 25.0);
        }

        public void ProcessKeyboardAccelerators(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            long seekAmount = 0;
            int volumeChange = 0;
            int direction = 0;
            var key = sender.Key;

            switch (key)
            {
                case VirtualKey.Left when VideoViewFocused:
                case VirtualKey.J:
                    direction = -1;
                    break;
                case VirtualKey.Right when VideoViewFocused:
                case VirtualKey.L:
                    direction = 1;
                    break;
                case VirtualKey.Up when VideoViewFocused:
                    volumeChange = 10;
                    break;
                case VirtualKey.Down when VideoViewFocused:
                    volumeChange = -10;
                    break;
                case VirtualKey.NumberPad0:
                case VirtualKey.NumberPad1:
                case VirtualKey.NumberPad2:
                case VirtualKey.NumberPad3:
                case VirtualKey.NumberPad4:
                case VirtualKey.NumberPad5:
                case VirtualKey.NumberPad6:
                case VirtualKey.NumberPad7:
                case VirtualKey.NumberPad8:
                case VirtualKey.NumberPad9:
                    SetTime(MediaPlayer.Length * (0.1 * (key - VirtualKey.NumberPad0)));
                    break;
                case VirtualKey.Number0:
                case VirtualKey.Number1:
                case VirtualKey.Number2:
                case VirtualKey.Number3:
                case VirtualKey.Number4:
                case VirtualKey.Number5:
                case VirtualKey.Number6:
                case VirtualKey.Number7:
                case VirtualKey.Number8:
                    SetWindowSize(0.25 * (key - VirtualKey.Number0));
                    return;
                case VirtualKey.Number9:
                    SetWindowSize(4);
                    return;
                case (VirtualKey)190:   // Period (".")
                    JumpFrame(false);
                    return;
                case (VirtualKey)188:   // Comma (",")
                    JumpFrame(true);
                    return;
            }

            switch (sender.Modifiers)
            {
                case VirtualKeyModifiers.Control:
                    seekAmount = 10000;
                    break;
                case VirtualKeyModifiers.Shift:
                    seekAmount = 1000;
                    break;
                case VirtualKeyModifiers.None:
                    seekAmount = 5000;
                    break;
            }

            if (seekAmount * direction != 0)
            {
                Seek(seekAmount * direction);
            }

            if (volumeChange != 0)
            {
                ChangeVolume(volumeChange);
            }
        }

        private void ShowControls()
        {
            ShowCursor();
            ControlsHidden = false;
        }

        private void HideControls()
        {
            ControlsHidden = true;
            HideCursor();
        }

        private void DelayHideControls()
        {
            _controlsVisibilityTimer.Debounce(() =>
            {
                if (MediaPlayer == null) return;
                if (MediaPlayer.IsPlaying && VideoViewFocused)
                {
                    HideControls();

                    // Workaround for PointerMoved is raised when show/hide cursor
                    OverrideVisibilityChange();
                }
            }, TimeSpan.FromSeconds(3));
        }

        private void OverrideVisibilityChange(int delay = 1000)
        {
            _visibilityOverride = true;
            Task.Delay(delay).ContinueWith(t => _visibilityOverride = false);
        }

        private void HideCursor()
        {
            var coreWindow = Window.Current.CoreWindow;
            if (coreWindow.PointerCursor?.Type == CoreCursorType.Arrow)
            {
                _cursor = coreWindow.PointerCursor;
                coreWindow.PointerCursor = null;
            }
        }

        private void ShowCursor()
        {
            var coreWindow = Window.Current.CoreWindow;
            if (coreWindow.PointerCursor == null)
            {
                coreWindow.PointerCursor = _cursor;
            }
        }

        private void SetTime(double time)
        {
            if (!MediaPlayer.IsSeekable || time < 0 || time > MediaPlayer.Length) return;
            if (MediaPlayer.State == VLCState.Ended)
            {
                Replay();
            }

            MediaPlayer.Time = (long)time;
        }

        public void OnSeekBarValueChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            if (MediaPlayer.IsSeekable)
            {
                double newTime = args.NewValue;
                if ((args.OldValue == MediaPlayer.Time || !MediaPlayer.IsPlaying) ||
                    !ShouldUpdateTime &&
                    newTime != MediaPlayer.Length)
                {
                    SetTime(newTime);
                }
            }
        }
    }
}
