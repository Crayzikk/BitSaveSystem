using System;

namespace BitSaveSystem
{
    /// <summary>Таймер автозбереження. Викликається з SaveManager.Update().</summary>
    public sealed class AutoSaveService
    {
        private readonly AutoSaveConfig _config;
        private readonly Func<bool> _isDirtyCheck;
        private readonly Action<string, SaveMode, bool, string> _saveCallback;
        // saveCallback(slotId, mode, captureScreenshot, screenshotName)

        private float _timer;

        public AutoSaveService(AutoSaveConfig config,
                               Func<bool> isDirtyCheck,
                               Action<string, SaveMode, bool, string> saveCallback)
        {
            _config       = config;
            _isDirtyCheck = isDirtyCheck;
            _saveCallback = saveCallback;
        }

        public void Tick(float deltaTime)
        {
            if (_config == null) return;
            if (_config.Mode != AutoSaveMode.Interval && _config.Mode != AutoSaveMode.Hybrid) return;

            _timer += deltaTime;
            if (_timer < _config.IntervalSeconds) return;
            _timer = 0f;

            if (_config.SkipIfNothingChanged && !_isDirtyCheck()) return;

            _saveCallback(_config.SlotId, _config.SaveMode, _config.CaptureScreenshot, null);
        }

        /// <summary>Викликати вручну для checkpoint-системи (Mode = OnTrigger / Hybrid).</summary>
        public void Trigger()
        {
            if (_config == null) return;
            if (_config.Mode != AutoSaveMode.OnTrigger && _config.Mode != AutoSaveMode.Hybrid) return;
            _saveCallback(_config.SlotId, _config.SaveMode, _config.CaptureScreenshot, null);
        }

        public void ResetTimer() => _timer = 0f;
    }
}
