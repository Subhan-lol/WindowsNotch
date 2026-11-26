using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace NotchIsland
{
    // ---- Minimal Core Audio interop (no NuGet needed) ----
    enum EDataFlow { eRender, eCapture, eAll }
    enum ERole { eConsole, eMultimedia, eCommunications }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct AUDIO_VOLUME_NOTIFICATION_DATA { public Guid guidEventContext; public int bMuted; public float fMasterVolume; public uint nChannels; public float firstChannel; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PROPERTYKEY key);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback cb);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback cb);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
        [PreserveSig] int VolumeStepUp(ref Guid ctx);
        [PreserveSig] int VolumeStepDown(ref Guid ctx);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float inc);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out uint count);
        [PreserveSig] int GetChannelsPeakValues(uint count, [Out, MarshalAs(UnmanagedType.LPArray)] float[] peaks);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
    }

    /// <summary>Default speaker volume + a live peak meter (one cheap COM call per read).</summary>
    class AudioService : IMMNotificationClient
    {
        class VolCallback : IAudioEndpointVolumeCallback
        {
            readonly AudioService owner;
            public VolCallback(AudioService o) { owner = o; }
            public int OnNotify(IntPtr p)
            {
                try
                {
                    var d = (AUDIO_VOLUME_NOTIFICATION_DATA)Marshal.PtrToStructure(p, typeof(AUDIO_VOLUME_NOTIFICATION_DATA));
                    owner.OnVolumeNotify(d.guidEventContext, d.bMuted != 0, d.fMasterVolume);
                }
                catch { }
                return 0;
            }
        }

        static Guid selfCtx = Guid.NewGuid();
        readonly Dispatcher ui;
        IMMDeviceEnumerator enumr;
        IAudioEndpointVolume vol;
        IAudioMeterInformation meter;
        VolCallback cb;

        public float Volume { get; private set; }
        public bool Muted { get; private set; }
        public bool Available { get { return vol != null; } }
        /// <summary>Raised on UI thread. Arg = true if the change came from this app.</summary>
        public event Action<bool> VolumeChanged;

        public AudioService(Dispatcher d) { ui = d; }

        public void Start()
        {
            try
            {
                enumr = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                enumr.RegisterEndpointNotificationCallback(this);
                Acquire();
            }
            catch { }
        }

        public void Stop()
        {
            try { if (enumr != null) enumr.UnregisterEndpointNotificationCallback(this); } catch { }
            ReleaseDevice();
        }

        void Acquire()
        {
            ReleaseDevice();
            try
            {
                IMMDevice dev;
                if (enumr.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out dev) != 0 || dev == null) return;
                object o;
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) == 0) vol = (IAudioEndpointVolume)o;
                iid = typeof(IAudioMeterInformation).GUID;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) == 0) meter = (IAudioMeterInformation)o;
                Marshal.ReleaseComObject(dev);
                if (vol != null)
                {
                    cb = new VolCallback(this);
                    vol.RegisterControlChangeNotify(cb);
                    float v; bool m;
                    vol.GetMasterVolumeLevelScalar(out v); vol.GetMute(out m);
                    Volume = v; Muted = m;
                }
            }
            catch { }
        }

        void ReleaseDevice()
        {
            try { if (vol != null && cb != null) vol.UnregisterControlChangeNotify(cb); } catch { }
            try { if (vol != null) Marshal.ReleaseComObject(vol); } catch { }
            try { if (meter != null) Marshal.ReleaseComObject(meter); } catch { }
            vol = null; meter = null; cb = null;
        }

        void OnVolumeNotify(Guid ctx, bool muted, float level)
        {
            bool self = ctx == selfCtx;
            ui.BeginInvoke(new Action(() =>
            {
                Volume = level; Muted = muted;
                var h = VolumeChanged; if (h != null) h(self);
            }));
        }

        /// <summary>Current output peak 0..1 (what's actually coming out of the speakers).</summary>
        public float Peak()
        {
            var m = meter;
            if (m == null) return 0;
            try { float p; if (m.GetPeakValue(out p) == 0) return p; } catch { }
            return 0;
        }

        public void SetVolume(float v)
        {
            if (vol == null) return;
            v = Math.Max(0, Math.Min(1, v));
            try
            {
                vol.SetMasterVolumeLevelScalar(v, ref selfCtx);
                if (Muted && v > 0) vol.SetMute(false, ref selfCtx);
            }
            catch { }
        }

        public void ToggleMute()
        {
            if (vol == null) return;
            try { vol.SetMute(!Muted, ref selfCtx); } catch { }
        }

        // IMMNotificationClient — called on a COM worker thread.
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string id)
        {
            if (flow == EDataFlow.eRender && role == ERole.eMultimedia)
                ui.BeginInvoke(new Action(() =>
                {
                    Acquire();
                    var h = VolumeChanged; if (h != null) h(true);
                }));
        }
        public void OnDeviceStateChanged(string id, int state) { }
        public void OnDeviceAdded(string id) { }
        public void OnDeviceRemoved(string id) { }
        public void OnPropertyValueChanged(string id, PROPERTYKEY key) { }
    }
}
