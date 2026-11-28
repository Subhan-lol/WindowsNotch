using System;
using System.Globalization;
using System.Linq;
using System.Speech.Recognition;
using System.Windows.Threading;

namespace NotchIsland
{
    /// <summary>
    /// Talk to Mochi: Windows' built-in (offline) speech recognizer in dictation mode.
    /// Shows words as you speak, stops by itself when you pause. Loaded on first use and
    /// released again after a while so it costs nothing when you're not talking.
    /// </summary>
    public class Voice
    {
        readonly Dispatcher ui;
        SpeechRecognitionEngine engine;
        readonly DispatcherTimer release;
        bool listening, gotResult;

        public event Action<string> Partial;   // words so far
        public event Action<string> Heard;     // final sentence
        public event Action<string> Failed;    // couldn't listen / didn't catch anything
        public event Action Ended;
        public event Action<int> Level;        // 0..100 mic level

        public bool Listening { get { return listening; } }

        public Voice(Dispatcher d)
        {
            ui = d;
            release = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            release.Tick += (s, e) => { release.Stop(); if (!listening) Dispose(); };
        }

        public static bool Supported
        {
            get { try { return SpeechRecognitionEngine.InstalledRecognizers().Count > 0; } catch { return false; } }
        }

        public bool Start()
        {
            if (listening) return true;
            try
            {
                if (engine == null) Create();
                engine.SetInputToDefaultAudioDevice();
                gotResult = false;
                engine.RecognizeAsync(RecognizeMode.Single);
                listening = true;
                release.Stop();
                return true;
            }
            catch (Exception ex)
            {
                listening = false;
                Raise(Failed, ex.Message.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0 || ex is InvalidOperationException
                    ? "I can't hear a microphone. Check Settings › Privacy › Microphone."
                    : "Voice didn't start: " + ex.Message);
                return false;
            }
        }

        /// <summary>Finish now and use what was heard so far.</summary>
        public void Stop() { if (listening && engine != null) try { engine.RecognizeAsyncStop(); } catch { } }

        public void Cancel() { if (listening && engine != null) { gotResult = true; try { engine.RecognizeAsyncCancel(); } catch { } } }

        void Create()
        {
            var all = SpeechRecognitionEngine.InstalledRecognizers();
            var info = all.FirstOrDefault(r => r.Culture.Name == CultureInfo.CurrentUICulture.Name)
                    ?? all.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                    ?? all.FirstOrDefault();
            if (info == null) throw new NotSupportedException("No speech recognizer is installed (Settings › Time & language › Speech).");
            engine = new SpeechRecognitionEngine(info);
            engine.LoadGrammar(new DictationGrammar());
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(6);
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(900);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(1200);
            engine.SpeechHypothesized += (s, e) => Raise(Partial, e.Result.Text);
            engine.SpeechRecognized += (s, e) => { gotResult = true; Raise(Heard, e.Result.Text); };
            engine.SpeechRecognitionRejected += (s, e) =>
            {
                if (e.Result != null && e.Result.Text.Length > 0 && e.Result.Confidence > 0.2f) { gotResult = true; Raise(Heard, e.Result.Text); }
            };
            engine.AudioLevelUpdated += (s, e) => { var h = Level; if (h != null) ui.BeginInvoke(new Action(() => h(e.AudioLevel))); };
            engine.RecognizeCompleted += (s, e) => ui.BeginInvoke(new Action(() =>
            {
                listening = false;
                if (!gotResult)
                {
                    var f = Failed;
                    if (f != null) f(e.Error != null ? "Voice error: " + e.Error.Message : e.InitialSilenceTimeout ? "I didn't hear anything." : "Sorry, I didn't catch that.");
                }
                var h = Ended; if (h != null) h();
                release.Start();
            }));
        }

        void Raise(Action<string> h, string text) { if (h != null) ui.BeginInvoke(new Action(() => h(text))); }

        public void Dispose()
        {
            try { if (engine != null) { engine.RecognizeAsyncCancel(); engine.Dispose(); } } catch { }
            engine = null;
            listening = false;
        }
    }
}
