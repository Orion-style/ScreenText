using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using ZenithApp.Core.Logging;

namespace ZenithApp.Modules.DynamicIsland;

public sealed class IslandMediaState
{
    public static readonly IslandMediaState Empty = new();

    public bool HasSession { get; init; }
    public bool IsPlaying { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string AppName { get; init; } = string.Empty;
    public ImageSource? Thumbnail { get; init; }
}

public sealed class IslandMediaService : IDisposable
{
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private bool _started;
    private bool _disposed;
    private int _refreshVersion;

    public IslandMediaService(IAppLogger logger)
    {
        _logger = logger;
    }

    public IslandMediaState CurrentState { get; private set; } = IslandMediaState.Empty;

    public event Action<IslandMediaState>? StateChanged;

    public async Task StartAsync()
    {
        if (_disposed) return;

        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed || !_started) return;

            _sessionManager = manager;
            _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
            _sessionManager.SessionsChanged += OnSessionsChanged;

            AttachToSession(_sessionManager.GetCurrentSession());
            await RefreshMediaStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[DynamicIsland] SMTC media integration unavailable: {ex.Message}");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
        }

        DetachCurrentSession();

        if (_sessionManager != null)
        {
            try
            {
                _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _sessionManager.SessionsChanged -= OnSessionsChanged;
            }
            catch
            {
                // Ignore COM teardown errors
            }
            _sessionManager = null;
        }

        UpdateState(IslandMediaState.Empty);
    }

    public async Task TogglePlayPauseAsync()
    {
        var session = _currentSession ?? _sessionManager?.GetCurrentSession();
        if (session == null) return;

        try
        {
            await session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[DynamicIsland] TogglePlayPause failed: {ex.Message}");
        }
    }

    public async Task NextTrackAsync()
    {
        var session = _currentSession ?? _sessionManager?.GetCurrentSession();
        if (session == null) return;

        try
        {
            await session.TrySkipNextAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[DynamicIsland] NextTrack failed: {ex.Message}");
        }
    }

    public async Task PreviousTrackAsync()
    {
        var session = _currentSession ?? _sessionManager?.GetCurrentSession();
        if (session == null) return;

        try
        {
            await session.TrySkipPreviousAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[DynamicIsland] PreviousTrack failed: {ex.Message}");
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        if (!_started || _disposed) return;
        AttachToSession(sender.GetCurrentSession());
        _ = RefreshMediaStateAsync();
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        if (!_started || _disposed) return;
        if (_currentSession == null)
        {
            AttachToSession(sender.GetCurrentSession());
        }
        _ = RefreshMediaStateAsync();
    }

    private void AttachToSession(GlobalSystemMediaTransportControlsSession? session)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_currentSession, session))
            {
                return;
            }

            DetachCurrentSessionLocked();
            _currentSession = session;
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            }
        }
    }

    private void DetachCurrentSession()
    {
        lock (_gate)
        {
            DetachCurrentSessionLocked();
        }
    }

    private void DetachCurrentSessionLocked()
    {
        if (_currentSession == null) return;

        try
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
        catch
        {
            // Ignore COM errors if source app already exited
        }
        _currentSession = null;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        if (!_started || _disposed) return;
        _ = RefreshMediaStateAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        if (!_started || _disposed) return;
        _ = RefreshMediaStateAsync();
    }

    private async Task RefreshMediaStateAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        var session = _currentSession ?? _sessionManager?.GetCurrentSession();
        if (session == null || !_started || _disposed)
        {
            UpdateState(IslandMediaState.Empty);
            return;
        }

        try
        {
            var playback = session.GetPlaybackInfo();
            var isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var props = await session.TryGetMediaPropertiesAsync();

            if (version != Volatile.Read(ref _refreshVersion) || !_started || _disposed)
            {
                return;
            }

            var title = props?.Title?.Trim() ?? string.Empty;
            var artist = props?.Artist?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
            {
                UpdateState(IslandMediaState.Empty);
                return;
            }

            ImageSource? thumb = null;
            if (props?.Thumbnail != null)
            {
                thumb = await LoadThumbnailAsync(props.Thumbnail);
            }

            if (version != Volatile.Read(ref _refreshVersion) || !_started || _disposed)
            {
                return;
            }

            UpdateState(new IslandMediaState
            {
                HasSession = true,
                IsPlaying = isPlaying,
                Title = string.IsNullOrWhiteSpace(title) ? "Без названия" : title,
                Artist = artist,
                AppName = FormatSourceAppName(session.SourceAppUserModelId),
                Thumbnail = thumb
            });
        }
        catch
        {
            if (version == Volatile.Read(ref _refreshVersion))
            {
                UpdateState(IslandMediaState.Empty);
            }
        }
    }

    private static async Task<ImageSource?> LoadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference thumbRef)
    {
        try
        {
            using var stream = await thumbRef.OpenReadAsync();
            using var netStream = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 72;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatSourceAppName(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId)) return string.Empty;
        var clean = appUserModelId.Split('!')[0];
        var dot = clean.LastIndexOf('.');
        if (dot >= 0 && dot < clean.Length - 1)
        {
            clean = clean[(dot + 1)..];
        }
        return clean.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateState(IslandMediaState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
