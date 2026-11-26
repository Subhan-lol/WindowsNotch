using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NotchIsland
{
    /// <summary>
    /// How Mochi moves on its own: smooth dancing locked to the music's beats, and idle fidgets.
    /// Everything here is an eased animation; nothing is set per-frame, so there's no jitter.
    /// </summary>
    public partial class Mascot
    {
        bool lively;
        double envSlow, envFast;
        DateTime lastBeat = DateTime.MinValue;
        int beatSide = 1;

        /// <summary>Breathes and fidgets more (taskbar buddy, expanded notch). Off for the tiny notch icon.</summary>
        public bool Lively { get { return lively; } set { lively = value; UpdateBreathing(); } }

        void UpdateBreathing()
        {
            if (lively && active)
            {
                var a = new DoubleAnimation(1, 1.022, TimeSpan.FromMilliseconds(2000)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Anim.InOutSine };
                Timeline.SetDesiredFrameRate(a, 8);
                breathe.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else breathe.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        static DoubleAnimationUsingKeyFrames Ease(params double[] msAndValues)
        {
            // pairs of (ms, value)
            var k = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            for (int i = 0; i + 1 < msAndValues.Length; i += 2)
                k.KeyFrames.Add(new EasingDoubleKeyFrame(msAndValues[i + 1], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(msAndValues[i])), Anim.InOutSine));
            return k;
        }

        // ================= dancing =================
        /// <summary>
        /// Feed the audio level (0..1) ~30x a second. It's smoothed into a slow and a fast envelope;
        /// a jump of the fast one over the slow one is a beat, and each beat plays one eased dance move.
        /// </summary>
        public void SetBeat(double level)
        {
            if (shown != Mood.Vibing) { if (beatApplied) ResetBeat(); return; }
            if (!beatApplied)
            {
                beatApplied = true;
                envSlow = envFast = level;
            }
            envFast += (level - envFast) * 0.6;
            envSlow += (level - envSlow) * (level > envSlow ? 0.05 : 0.02);
            double since = (DateTime.UtcNow - lastBeat).TotalMilliseconds;
            bool onset = envFast > envSlow * 1.1 + 0.015 && since > 300;
            bool keepGrooving = since > 700 && envSlow > 0.04; // steady songs with soft beats
            if (onset || keepGrooving)
            {
                lastBeat = DateTime.UtcNow;
                DanceMove(Math.Max(0.35, Math.Min(1, envFast * 1.4)));
            }
        }

        internal double DebugBob { get { return bob.Y; } }
        internal int DebugBeats;

        void DanceMove(double amp)
        {
            DebugBeats++;
            beatSide = -beatSide;
            // bounce: a little dip, then up, then settle
            double y0 = bob.Y, sy0 = squash.ScaleY, sx0 = squash.ScaleX;
            bool midAir = y0 < -1.5; // a new beat landed mid-bounce: glide down into the dip instead of snapping
            double dipAt = midAir ? 150 : 110;
            bob.BeginAnimation(TranslateTransform.YProperty, Ease(0, y0, dipAt, 1.0 * amp, dipAt + 130, -5 * amp, dipAt + 290, 0));
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, Ease(0, sy0, dipAt, 0.955, dipAt + 130, 1.03, dipAt + 290, 1));
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, Ease(0, sx0, dipAt, 1.025, dipAt + 130, 0.985, dipAt + 290, 1));
            // sway alternates sides beat by beat (held, so it rocks smoothly left-right)
            Anim.To(tilt, RotateTransform.AngleProperty, beatSide * 5 * amp, 320, Anim.InOutSine);
            Anim.To(earL, RotateTransform.AngleProperty, beatSide * 7 * amp, 260, Anim.InOutSine);
            Anim.To(earR, RotateTransform.AngleProperty, beatSide * 7 * amp, 260, Anim.InOutSine);
            Anim.To(tailRot, RotateTransform.AngleProperty, beatSide * 16 * amp, 300, Anim.InOutSine);
            if (!walking)
            {
                // arms pump alternately
                Anim.To(armL, RotateTransform.AngleProperty, ArmRestL + (beatSide > 0 ? 95 : 25) * amp, 240, Anim.OutCubic);
                Anim.To(armR, RotateTransform.AngleProperty, ArmRestR - (beatSide < 0 ? 95 : 25) * amp, 240, Anim.OutCubic);
            }
        }

        void ResetBeat()
        {
            beatApplied = false;
            Anim.To(tilt, RotateTransform.AngleProperty, 0, 300, Anim.InOutSine);
            Anim.To(earL, RotateTransform.AngleProperty, 0, 300, Anim.InOutSine);
            Anim.To(earR, RotateTransform.AngleProperty, 0, 300, Anim.InOutSine);
            Anim.To(tailRot, RotateTransform.AngleProperty, 0, 300, Anim.InOutSine);
            if (!walking)
            {
                Anim.To(armL, RotateTransform.AngleProperty, ArmRestL, 300, Anim.InOutSine);
                Anim.To(armR, RotateTransform.AngleProperty, ArmRestR, 300, Anim.InOutSine);
            }
        }

        // ================= idle life =================
        void OnAmbient(object sender, EventArgs e)
        {
            var m = shown;
            bool eyesOpen = m == Mood.Idle || m == Mood.Hungry || m == Mood.Focus || m == Mood.Worried || m == Mood.Excited;
            if (eyesOpen && rnd.NextDouble() < 0.75) Blink();
            if (Particles)
            {
                if (m == Mood.Vibing) Note();
                else if (m == Mood.Sleepy) Zzz();
            }
            bool calm = m == Mood.Idle || m == Mood.Happy || m == Mood.Focus;
            if (calm && !walking && !flashTimer.IsEnabled && handFlash == Prop.None && rnd.NextDouble() < (lively ? 0.85 : 0.35))
                Fidget(m == Mood.Focus);
            else if (m == Mood.Sleepy && rnd.NextDouble() < 0.3)
                earL.BeginAnimation(RotateTransform.AngleProperty, Pulse(earL.Angle, earL.Angle - 10, 90, 1, null)); // dreaming twitch
            int next = m == Mood.Sleepy ? 2600 : m == Mood.Vibing ? 1700 : lively ? 1500 + rnd.Next(2600) : 2400 + rnd.Next(3400);
            ambient.Interval = TimeSpan.FromMilliseconds(next);
        }

        /// <summary>One small idle thing. Focus mode keeps it subtle.</summary>
        void Fidget(bool subtle)
        {
            double r = rnd.NextDouble();
            if (!lively) { if (r < 0.5) EarTwitch(); else if (r < 0.8) TapFoot(); else Blink(); return; }
            if (subtle) { if (r < 0.4) LookAround(); else if (r < 0.7) EarTwitch(); else TapFoot(); return; }
            if (r < 0.16) LookAround();
            else if (r < 0.27) EarTwitch();
            else if (r < 0.38) TailWag();
            else if (r < 0.48) HeadTilt();
            else if (r < 0.57) ScratchEar();
            else if (r < 0.66) Groom();
            else if (r < 0.74) TapFoot();
            else if (r < 0.80) ShiftWeight();
            else if (r < 0.86 && Particles) Hum();
            else if (r < 0.90 && lively) ReachUp();
            else if (r < 0.94 && lively) { Jump(); Flash(Mood.Happy, 700); }
        }

        public void LookAround()
        {
            int s = rnd.NextDouble() < 0.5 ? 1 : -1;
            eyeLook.BeginAnimation(TranslateTransform.XProperty, Ease(0, eyeLook.X, 260, -3.3 * s, 900, -3.3 * s, 1200, 3.3 * s, 1900, 3.3 * s, 2200, 0));
            faceLook.BeginAnimation(TranslateTransform.XProperty, Ease(0, faceLook.X, 260, -1.4 * s, 900, -1.4 * s, 1200, 1.4 * s, 1900, 1.4 * s, 2200, 0));
        }

        public void EarTwitch()
        {
            var ear = rnd.NextDouble() < 0.5 ? earL : earR;
            double d = ear == earL ? -11 : 11;
            ear.BeginAnimation(RotateTransform.AngleProperty, Ease(0, ear.Angle, 70, ear.Angle + d, 140, ear.Angle, 230, ear.Angle + d * 0.7, 320, ear.Angle));
        }

        public void TailWag()
        {
            tailRot.BeginAnimation(RotateTransform.AngleProperty, Ease(0, 0, 250, 15, 500, -6, 750, 15, 1000, -6, 1250, 12, 1550, 0));
        }

        public void HeadTilt()
        {
            double a = rnd.NextDouble() < 0.5 ? 8 : -8;
            tilt.BeginAnimation(RotateTransform.AngleProperty, Ease(0, 0, 280, a, 1300, a, 1600, 0));
            eyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, Ease(0, eyeScale.ScaleY, 280, 1.1, 1300, 1.1, 1600, eyeScale.ScaleY));
        }

        /// <summary>Scratches behind an ear with a little paw, eyes squeezed happy.</summary>
        public void ScratchEar()
        {
            armR.BeginAnimation(RotateTransform.AngleProperty, Ease(0, armR.Angle, 220, -150, 330, -138, 440, -152, 550, -138, 660, -152, 770, -138, 1000, ArmRestR));
            tilt.BeginAnimation(RotateTransform.AngleProperty, Ease(0, 0, 220, 7, 770, 7, 1000, 0));
            earR.BeginAnimation(RotateTransform.AngleProperty, Ease(0, earR.Angle, 300, 10, 770, 10, 1000, 0));
            Flash(Mood.Happy, 1000);
        }

        /// <summary>Licks a paw like a cat.</summary>
        public void Groom()
        {
            armL.BeginAnimation(RotateTransform.AngleProperty, Ease(0, armL.Angle, 260, -65, 1500, -65, 1750, ArmRestL));
            bob.BeginAnimation(TranslateTransform.YProperty, Ease(0, 0, 300, 1.5, 450, 0.3, 600, 1.5, 750, 0.3, 900, 1.5, 1050, 0.3, 1200, 1.5, 1750, 0));
            tilt.BeginAnimation(RotateTransform.AngleProperty, Ease(0, 0, 260, -6, 1500, -6, 1750, 0));
            Flash(Mood.Happy, 1750);
        }

        public void TapFoot()
        {
            footR.BeginAnimation(TranslateTransform.YProperty, Ease(0, 0, 110, -3, 220, 0, 330, -3, 440, 0, 550, -3, 660, 0));
        }

        public void ShiftWeight()
        {
            double a = rnd.NextDouble() < 0.5 ? 3.5 : -3.5;
            tilt.BeginAnimation(RotateTransform.AngleProperty, Ease(0, 0, 500, a, 1400, a, 1900, 0));
            footL.BeginAnimation(TranslateTransform.YProperty, Ease(0, 0, 400, a > 0 ? -2 : 0, 1500, a > 0 ? -2 : 0, 1900, 0));
            footR.BeginAnimation(TranslateTransform.YProperty, Ease(0, 0, 400, a < 0 ? -2 : 0, 1500, a < 0 ? -2 : 0, 1900, 0));
        }

        public void Hum()
        {
            Flash(Mood.Happy, 1200);
            Note();
            tilt.BeginAnimation(RotateTransform.AngleProperty, Ease(0, 0, 300, 4, 600, -4, 900, 3, 1200, 0));
        }

        public void ReachUp()
        {
            Flash(Mood.Happy, 1500);
            armL.BeginAnimation(RotateTransform.AngleProperty, Ease(0, armL.Angle, 400, 165, 1100, 165, 1500, ArmRestL));
            armR.BeginAnimation(RotateTransform.AngleProperty, Ease(0, armR.Angle, 400, -165, 1100, -165, 1500, ArmRestR));
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, Ease(0, 1, 400, 1.07, 1100, 1.07, 1500, 1));
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, Ease(0, 1, 400, 0.95, 1100, 0.95, 1500, 1));
        }

        /// <summary>Both paws bouncing up and down, for juggling.</summary>
        public void JuggleArms(int ms)
        {
            var l = new DoubleAnimation(110, 160, TimeSpan.FromMilliseconds(300)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(ms)), FillBehavior = FillBehavior.Stop, EasingFunction = Anim.InOutSine };
            var r = new DoubleAnimation(-160, -110, TimeSpan.FromMilliseconds(300)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(ms)), FillBehavior = FillBehavior.Stop, EasingFunction = Anim.InOutSine };
            Timeline.SetDesiredFrameRate(l, 30); Timeline.SetDesiredFrameRate(r, 30);
            armL.BeginAnimation(RotateTransform.AngleProperty, l);
            armR.BeginAnimation(RotateTransform.AngleProperty, r);
        }
    }
}
