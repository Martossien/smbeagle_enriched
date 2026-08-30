using System;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace SMBeagle.Output
{
    /// <summary>
    /// Progression machine pour --progress-json : une ligne JSON sur stdout à
    /// chaque changement d'étape et toutes les ~2 s, puis un événement final
    /// « done » ou « error ». Étapes : discovery, shares, files, writing.
    /// </summary>
    public sealed class ProgressReporter : IDisposable
    {
        public const string STAGE_DISCOVERY = "discovery";
        public const string STAGE_SHARES = "shares";
        public const string STAGE_FILES = "files";
        public const string STAGE_WRITING = "writing";

        static readonly JsonSerializerOptions _json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>Rapporteur actif du processus, null sans --progress-json.</summary>
        public static ProgressReporter Current { get; set; }

        readonly TextWriter _out;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly object _lock = new();
        readonly Timer _timer;
        string _stage = STAGE_DISCOVERY;
        long _hosts, _shares, _files;
        bool _finished;

        public ProgressReporter(TextWriter output, int periodMs = 2000)
        {
            _out = output;
            _timer = new Timer(_ => Emit(), null, periodMs, periodMs);
        }

        public double ElapsedSeconds => Math.Round(_clock.Elapsed.TotalSeconds, 1);

        public void Stage(string stage)
        {
            lock (_lock)
                _stage = stage;
            Emit();
        }

        public void Counts(long? hosts = null, long? shares = null, long? files = null)
        {
            lock (_lock)
            {
                if (hosts.HasValue) _hosts = hosts.Value;
                if (shares.HasValue) _shares = shares.Value;
                if (files.HasValue) _files = files.Value;
            }
        }

        public void Files(long files) => Counts(files: files);

        void Emit()
        {
            lock (_lock)
            {
                if (_finished)
                    return;
                Write(new { @event = "progress", stage = _stage, hosts = _hosts, shares = _shares, files = _files, elapsed_s = ElapsedSeconds });
            }
        }

        public void Done(long files, string csv)
        {
            lock (_lock)
            {
                if (_finished)
                    return;
                _finished = true;
                Write(new { @event = "done", files, csv, elapsed_s = ElapsedSeconds });
            }
        }

        public void Error(string message)
        {
            lock (_lock)
            {
                if (_finished)
                    return;
                _finished = true;
                Write(new { @event = "error", message, elapsed_s = ElapsedSeconds });
            }
        }

        void Write(object payload)
        {
            _out.WriteLine(JsonSerializer.Serialize(payload, _json));
            _out.Flush();
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
